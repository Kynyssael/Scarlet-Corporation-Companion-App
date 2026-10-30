using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>Tonight's tallies (the bartender's drinks, the photographer's photos, the court's sessions), and past nights.</summary>
public class PosService
{
    private readonly Plugin plugin;
    private readonly PosStore store;
    private readonly PosData data;

    // Items added with + this session, newest last, for "Undo last". Not saved.
    private readonly List<Guid> recentSales = [];

    public PosService(Plugin plugin, PosStore store)
    {
        this.plugin = plugin;
        this.store = store;
        data = store.Load();
        FillMissingVenues();
    }

    // Nights started before nights carried a venue slug: work it out from the venue name,
    // or use the selected venue, so they can still be sent.
    private void FillMissingVenues()
    {
        var nights = data.Nights.Append(data.CurrentNight).Append(data.CurrentPhotoNight).Append(data.CurrentCourtNight).OfType<Night>().Where(n => n.VenueSlug.Length == 0).ToList();
        foreach (var night in nights)
        {
            night.VenueSlug = ScarletWebClient.Venues.FirstOrDefault(v => v.Name == night.Venue).Slug
                              ?? plugin.Configuration.VenueSlug;
        }
        if (nights.Count > 0)
            Save();
    }

    /// <summary>Tonight's bar tally; null until the first item is sold.</summary>
    public Night? CurrentNight => data.CurrentNight;

    /// <summary>Tonight's photo tally; null until the first photo is sold.</summary>
    public Night? CurrentPhotoNight => data.CurrentPhotoNight;

    public IReadOnlyList<Night> Nights => data.Nights;

    public void Save() => store.Save(data);

    public int CountSold(MenuItem item) => Count(data.CurrentNight, item.Id);

    public int CountSold(PhotoPackage photo) => Count(data.CurrentPhotoNight, photo.Id);

    private static int Count(Night? night, Guid id) =>
        night?.Lines.Where(l => l.MenuItemId == id).Sum(l => l.Quantity) ?? 0;

    /// <summary>Adds (or with a negative delta, removes) sold items from tonight's tally.</summary>
    public void Sell(MenuItem item, int delta)
    {
        if (delta < 0 && CountSold(item) == 0)
            return;

        Tally(data.CurrentNight ??= NewNight(Night.Bar), item.Id, item.Name, item.Price, delta);

        if (delta > 0)
            recentSales.Add(item.Id);
        else
            recentSales.Remove(recentSales.LastOrDefault(id => id == item.Id));

        Save();
    }

    /// <summary>Adds (or with a negative delta, removes) photo sessions from tonight's photo tally.</summary>
    public void Sell(PhotoPackage photo, int delta)
    {
        if (delta < 0 && CountSold(photo) == 0)
            return;

        Tally(data.CurrentPhotoNight ??= NewNight(Night.Photo), photo.Id, photo.Name, photo.Price, delta);
        Save();
    }

    // ---- Court sessions -------------------------------------------------------------------------

    /// <summary>Tonight's finished court sessions; null until the first session starts.</summary>
    public Night? CurrentCourtNight => data.CurrentCourtNight;

    public IReadOnlyList<CourtSession> RunningCourtSessions => data.RunningCourtSessions;

    /// <summary>Starts a session's timer. The room, if any, should already be locked on the website.</summary>
    public CourtSession StartCourtSession(CourtService service, string buyer, int minutes, int? roomId, string room)
    {
        // The night (and its venue) starts with the first session.
        data.CurrentCourtNight ??= NewNight(Night.Court);
        var session = new CourtSession
        {
            Service = service.Name,
            Rate = service.Rate,
            Buyer = buyer.Trim(),
            BoughtMinutes = minutes,
            RoomId = roomId,
            Room = room,
        };
        data.RunningCourtSessions.Add(session);
        Save();
        return session;
    }

    /// <summary>The client bought more time during the session.</summary>
    public void AddCourtTime(CourtSession session, int minutes)
    {
        session.BoughtMinutes = Math.Clamp(session.BoughtMinutes + minutes, MinCourtMinutes, MaxCourtMinutes);
        Save();
    }

    public const int MinCourtMinutes = 15;
    public const int MaxCourtMinutes = 12 * 60;

    /// <summary>Stops a session's timer and puts it on tonight's tally at its rate for the time bought.</summary>
    public void StopCourtSession(CourtSession session)
    {
        if (!data.RunningCourtSessions.Remove(session))
            return;

        var end = DateTime.Now;
        (data.CurrentCourtNight ??= NewNight(Night.Court)).Lines.Add(new OrderLine
        {
            MenuItemId = session.Id,
            Name = session.Service,
            UnitPrice = session.Cost,
            Quantity = 1,
            Buyer = session.Buyer,
            Room = session.Room,
            StartedAt = session.StartedAt,
            EndedAt = end,
            Rate = session.Rate,
            Minutes = session.BoughtMinutes,
        });
        Save();
    }

    /// <summary>Drops a session started by mistake, running or finished.</summary>
    public void RemoveCourtSession(Guid id)
    {
        data.RunningCourtSessions.RemoveAll(s => s.Id == id);
        data.CurrentCourtNight?.Lines.RemoveAll(l => l.MenuItemId == id);
        if (data.CurrentCourtNight is { Lines.Count: 0 } && data.RunningCourtSessions.Count == 0)
            data.CurrentCourtNight = null;
        Save();
    }

    /// <summary>Closes tonight's court sessions into the history. Not while a timer is running.</summary>
    public Night? EndCourtNight()
    {
        if (data.RunningCourtSessions.Count > 0)
            return null;
        var night = data.CurrentCourtNight;
        data.CurrentCourtNight = null;
        if (night != null)
            night.HouseCut = plugin.Configuration.CourtCut;
        return Close(night, "court sessions");
    }

    private Night NewNight(string kind) => new()
    {
        Kind = kind,
        HouseCut = kind switch
        {
            Night.Photo => plugin.Configuration.PhotoCut,
            Night.Court => plugin.Configuration.CourtCut,
            _ => 100,
        },
        Bartender = BartenderName(),
        Venue = plugin.Configuration.VenueName,
        VenueSlug = plugin.Configuration.VenueSlug,
    };

    private static void Tally(Night night, Guid id, string name, int price, int delta)
    {
        // A price change mid-night starts a new line so earlier sales keep their price.
        var line = night.Lines.FirstOrDefault(l => l.MenuItemId == id && l.UnitPrice == price);
        if (line == null && delta < 0)
            line = night.Lines.LastOrDefault(l => l.MenuItemId == id);

        if (line == null)
            night.Lines.Add(new OrderLine { MenuItemId = id, Name = name, UnitPrice = price, Quantity = delta });
        else if ((line.Quantity += delta) <= 0)
            night.Lines.Remove(line);
    }

    /// <summary>The item the last + was for, if it's still on tonight's tally.</summary>
    public MenuItem? LastSold =>
        recentSales.Count == 0 ? null : plugin.Configuration.Menu.FirstOrDefault(m => m.Id == recentSales[^1]);

    public void UndoLast()
    {
        if (LastSold is { } item)
            Sell(item, -1);
        else if (recentSales.Count > 0)
            recentSales.RemoveAt(recentSales.Count - 1); // Item gone from the menu; drop it.
    }

    /// <summary>Closes tonight's bar tally into the history and starts fresh.</summary>
    public Night? EndNight()
    {
        var night = data.CurrentNight;
        data.CurrentNight = null;
        recentSales.Clear();
        return Close(night, "items sold");
    }

    /// <summary>Closes tonight's photo tally into the history and starts fresh.</summary>
    public Night? EndPhotoNight()
    {
        var night = data.CurrentPhotoNight;
        data.CurrentPhotoNight = null;
        // The cut may have changed since the first photo; the site uses today's anyway.
        if (night != null)
            night.HouseCut = plugin.Configuration.PhotoCut;
        return Close(night, "photos taken");
    }

    private Night? Close(Night? night, string what)
    {
        if (night == null)
            return null;

        night.EndedAt = DateTime.Now;
        if (night.Lines.Count > 0)
            data.Nights.Add(night);
        Save();

        if (night.Lines.Count > 0 && plugin.Configuration.EchoNightTotal)
            Plugin.ChatGui.Print($"[{night.Venue}] {night.ItemCount} {what} tonight. You owe the venue {plugin.FormatGil(night.Owed)}.");

        plugin.Reports.Kick();
        return night;
    }

    private string BartenderName() =>
        Plugin.LocalPlayerName() ?? plugin.Auth.State.Name ?? string.Empty;
}
