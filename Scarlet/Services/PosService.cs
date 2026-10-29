using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>The bartender's tally for tonight, and past nights.</summary>
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
        var nights = data.Nights.Append(data.CurrentNight).OfType<Night>().Where(n => n.VenueSlug.Length == 0).ToList();
        foreach (var night in nights)
        {
            night.VenueSlug = ScarletWebClient.Venues.FirstOrDefault(v => v.Name == night.Venue).Slug
                              ?? plugin.Configuration.VenueSlug;
        }
        if (nights.Count > 0)
            Save();
    }

    /// <summary>Tonight's tally; null until the first item is sold.</summary>
    public Night? CurrentNight => data.CurrentNight;

    public IReadOnlyList<Night> Nights => data.Nights;

    public void Save() => store.Save(data);

    public int CountSold(MenuItem item) =>
        data.CurrentNight?.Lines.Where(l => l.MenuItemId == item.Id).Sum(l => l.Quantity) ?? 0;

    /// <summary>Adds (or with a negative delta, removes) sold items from tonight's tally.</summary>
    public void Sell(MenuItem item, int delta)
    {
        if (delta < 0 && CountSold(item) == 0)
            return;

        var night = data.CurrentNight ??= new Night
        {
            Bartender = BartenderName(),
            Venue = plugin.Configuration.VenueName,
            VenueSlug = plugin.Configuration.VenueSlug,
        };

        // A price change mid-night starts a new line so earlier sales keep their price.
        var line = night.Lines.FirstOrDefault(l => l.MenuItemId == item.Id && l.UnitPrice == item.Price);
        if (line == null && delta < 0)
            line = night.Lines.LastOrDefault(l => l.MenuItemId == item.Id);

        if (line == null)
            night.Lines.Add(new OrderLine { MenuItemId = item.Id, Name = item.Name, UnitPrice = item.Price, Quantity = delta });
        else if ((line.Quantity += delta) <= 0)
            night.Lines.Remove(line);

        if (delta > 0)
            recentSales.Add(item.Id);
        else
            recentSales.Remove(recentSales.LastOrDefault(id => id == item.Id));

        Save();
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

    /// <summary>Closes tonight's tally into the history and starts fresh.</summary>
    public Night? EndNight()
    {
        if (data.CurrentNight is not { } night)
            return null;

        night.EndedAt = DateTime.Now;
        if (night.Lines.Count > 0)
            data.Nights.Add(night);
        data.CurrentNight = null;
        recentSales.Clear();
        Save();

        if (night.Lines.Count > 0 && plugin.Configuration.EchoNightTotal)
            Plugin.ChatGui.Print($"[{night.Venue}] {night.ItemCount} items sold tonight. You owe the venue {plugin.FormatGil(night.Total)}.");

        plugin.Reports.Kick();
        return night;
    }

    private string BartenderName() =>
        Plugin.LocalPlayerName() ?? plugin.Auth.State.Name ?? string.Empty;
}
