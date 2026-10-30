using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Models;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// Courts (companions): log each session with a buyer, like the venue's Companion/Room Timers sheet.
/// The court enters the time the client buys, which sets the price (hourly rate × time bought); the
/// timer only shows time left. Starting a session locks the private room on the website, stopping it
/// frees the room, and the court owes the venue the house cut.
/// </summary>
public class CourtPage(Plugin plugin)
{
    private const string EndNightPopup = "End the court night?##scarletEndCourt";

    // The "new session" form.
    private string buyer = string.Empty;
    private int? serviceId;
    private int? roomId;
    private int boughtMinutes = 60;

    /// <summary>Time bought goes up and down in half hours.</summary>
    private const int TimeStep = 30;

    // Waiting for the website to lock the room before the timer starts.
    private bool starting;
    private string? problem;

    private bool openEndNight;

    public void Draw()
    {
        // Private rooms and who has them, for the room picker.
        plugin.RoomLocks.RefreshIfStale();

        var night = plugin.Pos.CurrentCourtNight;
        Theme.PageHeader("Companion / room timers", night != null ? $"since {night.StartedAt:t}" : "start a session when you go with a buyer");

        UnsentNotice.Draw(plugin);

        if (plugin.Configuration.CourtServices.Count == 0)
        {
            Theme.Empty(plugin.WebSync.Syncing
                ? "Loading court rates from the website..."
                : "No court rates for this venue yet. Management adds them in the website admin (Court rates), then sync.", FontAwesomeIcon.Heart);
        }
        else
        {
            DrawNewSession();
        }

        ImGuiHelpers.ScaledDummy(4);
        var footer = Theme.OwedHeight() + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
        using (var table = ImRaii.Child("##courtSessions", new Vector2(0, -footer), false))
        {
            if (table.Success)
                DrawSessions(night);
        }

        DrawTotals(night);
        DrawEndNightPopup();
    }

    // ---- New session --------------------------------------------------------------------------

    private void DrawNewSession()
    {
        var services = plugin.Configuration.CourtServices;
        var service = services.FirstOrDefault(s => s.RemoteId == serviceId) ?? services[0];
        serviceId = service.RemoteId;

        var rooms = FreeRooms();
        var room = rooms.FirstOrDefault(r => r.Id == roomId);
        if (room == null)
            roomId = null;

        var style = ImGui.GetStyle();
        // Section label, field labels and the price line, plus the row of fields.
        var lines = problem != null ? 4 : 3;
        var height = ImGui.GetTextLineHeightWithSpacing() * lines + ImGui.GetFrameHeightWithSpacing() + style.CellPadding.Y * 4 + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##newSession", new Vector2(0, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Scarlet);

        Theme.SectionLabel("New session");

        // Labels over the fields, in columns.
        var spacing = style.ItemSpacing.X;
        var button = 110 * ImGuiHelpers.GlobalScale;
        var time = ImGui.GetFrameHeight() * 2 + ImGui.CalcTextSize("00h00").X + spacing * 2;
        using (var form = ImRaii.Table("##form", 5, ImGuiTableFlags.SizingFixedFit))
        {
            if (!form.Success)
                return;
            ImGui.TableSetupColumn("Buyer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, time);
            ImGui.TableSetupColumn("Service", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Start", ImGuiTableColumnFlags.WidthFixed, button);

            ImGui.TableNextRow();
            foreach (var label in new[] { "Buyer", "Time bought", "Service", "Private room", "" })
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled(label);
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##buyer", "Buyer's name", ref buyer, 64);

            // Time bought: what the session costs.
            ImGui.TableNextColumn();
            DrawTimeBought();

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            using (var combo = ImRaii.Combo("##service", $"{service.Name} ({plugin.FormatGil(service.Rate)}/h)"))
            {
                if (combo.Success)
                {
                    foreach (var option in services)
                    {
                        if (ImGui.Selectable($"{option.Name} ({plugin.FormatGil(option.Rate)}/h)##{option.RemoteId}", option.RemoteId == serviceId))
                            serviceId = option.RemoteId;
                        if (option.Description.Length > 0 && ImGui.IsItemHovered())
                            ImGui.SetTooltip(option.Description);
                    }
                }
            }

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            using (var combo = ImRaii.Combo("##room", room?.Name ?? "No room"))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable("No room", roomId == null))
                        roomId = null;
                    foreach (var option in rooms)
                    {
                        if (ImGui.Selectable($"{option.Name}##{option.Id}", option.Id == roomId))
                            roomId = option.Id;
                    }
                    if (plugin.RoomLocks.Rooms == null)
                        ImGui.TextDisabled(plugin.RoomLocks.Loading ? "Loading rooms..." : "Rooms not loaded");
                    else if (rooms.Count == 0)
                        ImGui.TextDisabled("No free private room");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The private room is locked on the website while the session runs.");

            ImGui.TableNextColumn();
            using (ImRaii.Disabled(starting || buyer.Trim().Length == 0))
            {
                using var colors = Theme.Primary();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, starting ? "Locking..." : "Start", new Vector2(button, 0)))
                    Start(service, room);
            }
            if (buyer.Trim().Length == 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Type the buyer's name first.");
        }

        ImGui.TextDisabled($"{Hours(boughtMinutes)} of {service.Name} at {plugin.FormatGil(service.Rate)}/h =");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
            ImGui.Text(plugin.FormatGil(CourtSession.Price(service.Rate, boughtMinutes)));

        if (problem != null)
            Theme.ErrorText(problem);
    }

    /// <summary>[-] 1h30 [+], in half hours.</summary>
    private void DrawTimeBought()
    {
        using (ImRaii.Disabled(boughtMinutes <= TimeStep))
        {
            if (ImGuiComponents.IconButton("lessTime", FontAwesomeIcon.Minus))
                boughtMinutes -= TimeStep;
        }
        ImGui.SameLine();
        var text = Hours(boughtMinutes);
        var width = ImGui.CalcTextSize("00h00").X;
        var cursor = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursor + (width - ImGui.CalcTextSize(text).X) / 2);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(text);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Time the client is buying. This sets the price; the timer only shows time left.");
        ImGui.SameLine();
        ImGui.SetCursorPosX(cursor + width + ImGui.GetStyle().ItemSpacing.X);
        using (ImRaii.Disabled(boughtMinutes + TimeStep > PosService.MaxCourtMinutes))
        {
            if (ImGuiComponents.IconButton("moreTime", FontAwesomeIcon.Plus))
                boughtMinutes += TimeStep;
        }
    }

    /// <summary>Private rooms nobody else has locked (one already locked by you counts as free).</summary>
    private List<RoomDto> FreeRooms()
    {
        var inUse = plugin.Pos.RunningCourtSessions.Select(s => s.RoomId).OfType<int>().ToHashSet();
        return (plugin.RoomLocks.Rooms ?? [])
            .Where(r => r.Kind == "private" && (!r.Locked || r.Mine) && !inUse.Contains(r.Id))
            .ToList();
    }

    private void Start(CourtService service, RoomDto? room)
    {
        problem = null;
        var who = buyer.Trim();
        var minutes = boughtMinutes;
        if (room == null)
        {
            plugin.Pos.StartCourtSession(service, who, minutes, null, string.Empty);
            buyer = string.Empty;
            return;
        }

        // Lock first: a room someone else just took shouldn't start a timer. LockAsync reads the
        // character name, so it has to be called here on the main thread; only the wait is async.
        starting = true;
        var note = $"{service.Name} with {who} until {DateTime.Now.AddMinutes(minutes):t}";
        _ = StartAfterLockAsync(plugin.RoomLocks.LockAsync(room.Id, note), service, who, minutes, room);
    }

    private async Task StartAfterLockAsync(Task<bool> locking, CourtService service, string who, int minutes, RoomDto room)
    {
        var locked = false;
        try
        {
            locked = await locking;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Room lock failed");
        }

        await Plugin.Framework.RunOnFrameworkThread(() =>
        {
            starting = false;
            if (!locked)
            {
                problem = $"Couldn't lock {room.Name}: {plugin.RoomLocks.Error ?? "unknown error"}";
                return;
            }
            plugin.Pos.StartCourtSession(service, who, minutes, room.Id, room.Name);
            buyer = string.Empty;
            roomId = null;
        });
    }

    // ---- Sessions table -----------------------------------------------------------------------

    private void DrawSessions(Night? night)
    {
        var running = plugin.Pos.RunningCourtSessions;
        var done = night?.Lines ?? [];
        if (running.Count == 0 && done.Count == 0)
        {
            Theme.Empty("No sessions yet tonight.", FontAwesomeIcon.Hourglass);
            return;
        }

        using var table = ImRaii.Table("##timers", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Companion", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableSetupColumn("Buyer", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableSetupColumn("Service", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableSetupColumn("IN", ImGuiTableColumnFlags.None, 0.7f);
        ImGui.TableSetupColumn("OUT", ImGuiTableColumnFlags.None, 1.5f);
        ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("Hours", ImGuiTableColumnFlags.None, 0.6f);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        var companion = night?.Bartender ?? Plugin.LocalPlayerName() ?? string.Empty;
        var now = DateTime.Now;

        // Running sessions first; ToList because Stop changes the list.
        foreach (var session in running.ToList())
        {
            using var id = ImRaii.PushId(session.Id.ToString());
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
            var left = (int)Math.Ceiling((session.EndsAt - now).TotalMinutes);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32((left >= 0 ? Theme.Green : Theme.Red) with { W = 0.07f }));
            Cells(companion, session.Buyer, session.Service, session.StartedAt, session.Room, session.BoughtMinutes, session.Cost);
            // OUT: when the time bought runs out, and how long is left.
            ImGui.Text(session.EndsAt.ToString("t"));
            ImGui.SameLine();
            Theme.Pill(left >= 0 ? $"{left} min left" : $"{-left} min over", left >= 0 ? Theme.Green : Theme.Red);
            if (left < 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip("Past the time bought. +30 min if the client buys more, or Stop.");
            ImGui.TableSetColumnIndex(8);

            if (ImGui.Button($"+{TimeStep}"))
                plugin.Pos.AddCourtTime(session, TimeStep);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"The client buys {TimeStep} more minutes (+{plugin.FormatGil(CourtSession.Price(session.Rate, TimeStep))}).");
            ImGui.SameLine();
            using (Theme.Primary())
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, "Stop", ImGuiHelpers.ScaledVector2(64, 0)))
                    Stop(session);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(session.RoomId != null ? $"End the session and unlock {session.Room}." : "End the session.");
            ImGui.SameLine();
            RemoveButton(session.Id, session.RoomId);
        }

        foreach (var line in done.ToList())
        {
            using var id = ImRaii.PushId(line.MenuItemId.ToString());
            ImGui.TableNextRow();
            var start = line.StartedAt ?? now;
            var end = line.EndedAt ?? start;
            var bought = line.Minutes ?? CourtSession.Minutes(start, end);
            Cells(companion, line.Buyer ?? string.Empty, line.Name, start, line.Room ?? string.Empty, bought, line.UnitPrice);
            ImGui.Text(end.ToString("t"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Ran {Hours(CourtSession.Minutes(start, end))}.");
            ImGui.TableSetColumnIndex(8);
            RemoveButton(line.MenuItemId, null);
        }
    }

    /// <summary>Every column but OUT and the buttons; leaves the cursor in the OUT column.</summary>
    private void Cells(string companion, string who, string service, DateTime start, string room, int boughtMinutes, long cost)
    {
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(companion);
        ImGui.TableNextColumn();
        ImGui.Text(who);
        ImGui.TableNextColumn();
        ImGui.Text(service);
        ImGui.TableNextColumn();
        ImGui.Text(start.ToString("t"));
        ImGui.TableSetColumnIndex(5);
        if (room.Length > 0)
            ImGui.Text(room);
        else
            ImGui.TextDisabled("-");
        ImGui.TableNextColumn();
        ImGui.Text(Hours(boughtMinutes));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Time bought");
        ImGui.TableNextColumn();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
            ImGui.Text(plugin.FormatGil(cost));
        ImGui.TableSetColumnIndex(4);
    }

    private void Stop(CourtSession session)
    {
        plugin.Pos.StopCourtSession(session);
        Unlock(session.RoomId, session.Room);
    }

    /// <summary>Removes a session started by mistake. Ctrl+click, so it can't happen by accident.</summary>
    private void RemoveButton(Guid sessionId, int? lockedRoom)
    {
        var ctrl = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrl))
        {
            if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash))
            {
                var room = plugin.Pos.RunningCourtSessions.FirstOrDefault(s => s.Id == sessionId)?.Room ?? string.Empty;
                plugin.Pos.RemoveCourtSession(sessionId);
                Unlock(lockedRoom, room);
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ctrl ? "Remove this session (started by mistake)." : "Hold Ctrl and click to remove this session.");
    }

    private void Unlock(int? id, string room)
    {
        if (id is not { } roomToFree)
            return;
        var unlocking = plugin.RoomLocks.UnlockAsync(roomToFree);
        _ = unlocking.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || !t.Result)
                problem = $"Couldn't unlock {room}: {plugin.RoomLocks.Error}. Unlock it from the Rooms tab.";
        }, TaskScheduler.Default);
    }

    // ---- Totals -------------------------------------------------------------------------------

    private void DrawTotals(Night? night)
    {
        var cut = plugin.Configuration.CourtCut;
        var total = night?.Total ?? 0;
        var owed = Night.OwedFrom(total, cut);
        var buttonWidth = 160 * ImGuiHelpers.GlobalScale;

        Theme.TotalsCard("##courtTotals", $"You owe the venue ({cut}% house cut)", plugin.FormatGil(owed),
            $"{Sessions(night)} · {plugin.FormatGil(total)} in sales · you keep {plugin.FormatGil(total - owed)}",
            buttonWidth, () =>
            {
                var running = plugin.Pos.RunningCourtSessions.Count;
                using (ImRaii.Disabled(running > 0 || night == null || night.Lines.Count == 0))
                {
                    using var colors = Theme.Primary();
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Moon, "End night", new Vector2(buttonWidth, 0)))
                        openEndNight = true;
                }
                if (running > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Stop the running sessions first.");
            });
    }

    private void DrawEndNightPopup()
    {
        if (openEndNight)
        {
            ImGui.OpenPopup(EndNightPopup);
            openEndNight = false;
        }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        using var popup = ImRaii.PopupModal(EndNightPopup, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup.Success)
            return;

        var night = plugin.Pos.CurrentCourtNight;
        var cut = plugin.Configuration.CourtCut;
        ImGui.Text($"{Sessions(night)} tonight, {plugin.FormatGil(night?.Total ?? 0)} in sales.");
        ImGui.Text($"You owe the venue ({cut}%)");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
            ImGui.Text(plugin.FormatGil(Night.OwedFrom(night?.Total ?? 0, cut)));
        ImGui.TextDisabled("The night is sent to the website for management.");
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (Theme.Primary())
        {
            if (ImGui.Button("End night", button))
            {
                plugin.Pos.EndCourtNight();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Keep going", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    private static string Hours(int minutes) => $"{minutes / 60}h{minutes % 60:00}";

    private static string Sessions(Night? night) =>
        (night?.ItemCount ?? 0) switch
        {
            1 => "1 session",
            var n => $"{n} sessions",
        };
}
