using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// Photographers (and management): the venue's service rooms from the website, and who has
/// which one locked. Lock a room while you use it so nobody walks into a shoot.
/// </summary>
public class RoomsPage(Plugin plugin)
{
    // What was typed as a note for each room, before locking it.
    private readonly Dictionary<int, string> notes = [];

    public void Draw()
    {
        var source = plugin.RoomLocks;
        source.RefreshIfStale();

        DrawHeader(source);

        if (source.Error != null)
            Theme.ErrorText(source.Error);

        if (source.Rooms is not { } loaded)
        {
            Theme.Empty(source.Loading ? "Loading rooms from the website..." : "Nothing loaded yet.", FontAwesomeIcon.DoorClosed);
            return;
        }

        var manager = plugin.Auth.CurrentAccess.Manage;
        // The website already only sends photographers photo rooms and courts private rooms; filter
        // here too so the dev "View as" previews on a management account match.
        var rooms = loaded.Where(r => Visible(r.Kind)).ToList();

        if (rooms.Count == 0)
        {
            Theme.Empty($"This venue has no {RoomsWord()} yet. {(manager ? "Add them" : "Management adds them")} in the website admin (Service rooms).", FontAwesomeIcon.DoorClosed);
            return;
        }

        var mine = rooms.Where(r => r.Mine).ToList();
        if (mine.Count > 0)
        {
            Theme.Icon(FontAwesomeIcon.Key, Theme.Rose);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Rose))
                ImGui.TextWrapped($"You have {string.Join(", ", mine.Select(r => r.Name))} locked. Unlock when you're done.");
        }

        using var list = ImRaii.Child("##rooms", Vector2.Zero, false);
        if (!list.Success)
            return;

        // Photo rooms first, then private rooms.
        foreach (var room in rooms.OrderBy(r => r.Kind == "photo" ? 0 : 1))
            DrawRoom(room, source, manager);
    }

    // Photographers deal with photo rooms, courts with private rooms, management with both.
    private bool Visible(string kind)
    {
        var access = plugin.Auth.CurrentAccess;
        return kind == "photo" ? access.Photos : access.Court;
    }

    private string RoomsWord() => (Visible("photo"), Visible("private")) switch
    {
        (true, false) => "photo rooms",
        (false, true) => "private rooms",
        _ => "rooms",
    };

    private void DrawHeader(RoomLocks source)
    {
        var refresh = 90 * ImGuiHelpers.GlobalScale;
        Theme.PageHeader($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} {RoomsWord()}",
            source.Loading ? "refreshing..." : source.UpdatedAt is { } at ? $"from the website, {at:t}" : null,
            refresh, () =>
            {
                using (ImRaii.Disabled(source.Loading))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh", new Vector2(refresh, 0)))
                        source.Refresh();
                }
            });
    }

    private void DrawRoom(RoomDto room, RoomLocks source, bool manager)
    {
        using var id = ImRaii.PushId(room.Id);
        float nameLine;
        using (Theme.NameFont())
            nameLine = ImGui.GetTextLineHeight();
        var style = ImGui.GetStyle();
        var height = nameLine + ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetFrameHeight() + style.ItemSpacing.Y * 2 + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##room", new Vector2(0, height), room.Mine);
        if (!card.Success)
            return;

        var status = !room.Locked ? Theme.Green : room.Mine ? Theme.Rose : Theme.Red;
        Theme.CardAccent(status);

        // Name and type tag.
        using (Theme.NameFont())
            ImGui.Text(room.Name);
        if (room.Description.Length > 0 && ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.Text(room.Description);
        }
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (nameLine - ImGui.GetTextLineHeight()) / 2);
        Theme.Pill(room.KindLabel, room.Kind == "photo" ? Theme.Rose : Theme.Gold);

        // Status.
        Theme.Dot(status);
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, status))
            ImGui.Text(!room.Locked ? "Free" : room.Mine ? "Locked by you" : $"Locked by {room.LockedBy}");
        if (room.Locked && room.LockedAt is { } since)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"since {since.ToLocalTime():t} ({Ago(since)})");
        }
        if (room.Locked && room.LockedNote.Length > 0)
            ImGui.TextDisabled($"\"{room.LockedNote}\"");
        else
            ImGui.NewLine();

        DrawActions(room, source, manager);
    }

    private void DrawActions(RoomDto room, RoomLocks source, bool manager)
    {
        var busy = source.Busy == room.Id;
        using var disabled = ImRaii.Disabled(source.Busy != null);

        if (!room.Locked)
        {
            var note = notes.GetValueOrDefault(room.Id, string.Empty);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 110 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##note", "Note (optional), e.g. \"Shoot with Ayame until 10\"", ref note, 140))
                notes[room.Id] = note;
            ImGui.SameLine();
            using var colors = Theme.Primary();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Lock, busy ? "Locking..." : "Lock", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                source.Lock(room, note);
                notes.Remove(room.Id);
            }
        }
        else if (room.Mine)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.LockOpen, busy ? "Unlocking..." : "Unlock", ImGuiHelpers.ScaledVector2(110, 0)))
                source.Unlock(room);
        }
        else if (manager)
        {
            // Someone forgot to unlock: management can free the room.
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.LockOpen, busy ? "Unlocking..." : "Unlock", ImGuiHelpers.ScaledVector2(110, 0)))
                source.Unlock(room);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Management: free the room {room.LockedBy} locked.");
        }
        else
        {
            ImGui.TextDisabled($"Ask {room.LockedBy} or management to unlock it.");
        }
    }

    private static string Ago(DateTimeOffset since)
    {
        var span = DateTimeOffset.Now - since;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalHours} h {span.Minutes} min";
    }
}
