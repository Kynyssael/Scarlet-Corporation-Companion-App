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
    private static readonly Vector4 Scarlet = new(0.70f, 0.07f, 0.12f, 1f);
    private static readonly Vector4 ScarletHover = new(0.82f, 0.15f, 0.20f, 1f);
    private static readonly Vector4 ScarletActive = new(0.58f, 0.04f, 0.09f, 1f);
    private static readonly Vector4 Rose = new(1f, 0.62f, 0.66f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.45f, 1f);

    // What the photographer typed as a note for each room, before locking it.
    private readonly Dictionary<int, string> notes = [];

    public void Draw()
    {
        var source = plugin.RoomLocks;
        source.RefreshIfStale();

        DrawHeader(source);

        if (source.Error != null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Red))
                ImGui.TextWrapped(source.Error);
        }

        if (source.Rooms is not { } rooms)
        {
            ImGui.TextDisabled(source.Loading ? "Loading rooms from the website..." : "Nothing loaded yet.");
            return;
        }

        if (rooms.Count == 0)
        {
            // The website only sends photographers the photo rooms.
            ImGui.TextDisabled(plugin.Auth.CurrentAccess.Manage
                ? "This venue has no service rooms yet. Add them in the website admin (Service rooms)."
                : "This venue has no photo rooms yet. Management adds them in the website admin (Service rooms).");
            return;
        }

        var mine = rooms.Where(r => r.Mine).ToList();
        if (mine.Count > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Rose))
                ImGui.TextWrapped($"You have {string.Join(", ", mine.Select(r => r.Name))} locked. Unlock when you're done.");
        }

        using var list = ImRaii.Child("##rooms", Vector2.Zero, false);
        if (!list.Success)
            return;

        var manager = plugin.Auth.CurrentAccess.Manage;
        // Photo rooms first: they're what photographers come here for.
        foreach (var room in rooms.OrderBy(r => r.Kind == "photo" ? 0 : 1))
            DrawRoom(room, source, manager);
    }

    private void DrawHeader(RoomLocks source)
    {
        ImGui.Text($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} {(plugin.Auth.CurrentAccess.Manage ? "rooms" : "photo rooms")}");
        ImGui.SameLine();
        ImGui.TextDisabled(source.Loading ? "refreshing..." : source.UpdatedAt is { } at ? $"from the website, {at:t}" : string.Empty);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - 90 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(source.Loading))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh", ImGuiHelpers.ScaledVector2(90, 0)))
                source.Refresh();
        }
        ImGui.Separator();
    }

    private void DrawRoom(RoomDto room, RoomLocks source, bool manager)
    {
        using var id = ImRaii.PushId(room.Id);
        var height = ImGui.GetTextLineHeightWithSpacing() * 3 + ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2;
        using var card = ImRaii.Child("##room", new Vector2(0, height), true);
        if (!card.Success)
            return;

        // Name and type tag.
        ImGui.Text(room.Name);
        if (room.Description.Length > 0 && ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.Text(room.Description);
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, room.Kind == "photo" ? Rose : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            ImGui.Text($"[{room.KindLabel}]");

        // Status.
        if (!room.Locked)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Green))
                ImGui.Text("Free");
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, room.Mine ? Rose : Red))
                ImGui.Text(room.Mine ? "Locked by you" : $"Locked by {room.LockedBy}");
            if (room.LockedAt is { } since)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"since {since.ToLocalTime():t} ({Ago(since)})");
            }
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
            using var colors = ImRaii.PushColor(ImGuiCol.Button, Scarlet).Push(ImGuiCol.ButtonHovered, ScarletHover).Push(ImGuiCol.ButtonActive, ScarletActive);
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
