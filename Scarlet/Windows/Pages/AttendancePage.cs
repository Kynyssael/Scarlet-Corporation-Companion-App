using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// Management only: who worked when, and what they're owed. Managers start a shift when someone comes
/// on and end it when they leave (or type in a past shift); each shift pays the role's hourly rate from
/// the website admin (Pay rates), by the whole hour: 45 minutes or more of a part hour counts as an
/// hour. Tick Paid when you pay them.
/// </summary>
public class AttendancePage(Plugin plugin)
{
    private const string EditPopup = "Edit shift##scarletEditShift";

    private static readonly (string Key, string Label)[] Filters =
        [("unpaid", "Not paid"), ("paid", "Paid"), ("all", "Everything")];

    private string filter = "unpaid";
    private string search = string.Empty;

    // The "record a shift" form.
    private string newName = string.Empty;
    private string newRole = string.Empty;
    private string newIn = DateInput.Now();
    private string newOut = string.Empty;
    private string newNote = string.Empty;

    // The edit popup.
    private ShiftDto? editing;
    private string editName = string.Empty;
    private string editRole = string.Empty;
    private string editIn = string.Empty;
    private string editOut = string.Empty;
    private string editNote = string.Empty;
    private bool openEdit;

    public void Draw()
    {
        var source = plugin.Shifts;
        source.RefreshIfStale();

        var refresh = 90 * ImGuiHelpers.GlobalScale;
        Theme.PageHeader($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} attendance",
            source.Loading ? "refreshing..." : source.UpdatedAt is { } at ? $"from the website, {at:t}" : null,
            refresh, () =>
            {
                using (ImRaii.Disabled(source.Loading))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh", new Vector2(refresh, 0)))
                        source.Refresh();
                }
            });

        if (source.Error != null)
            Theme.ErrorText(source.Error);

        if (source.Shifts is not { } shifts || source.Rates is not { } rates)
        {
            Theme.Empty(source.Loading ? "Loading attendance from the website..." : "Nothing loaded yet.", FontAwesomeIcon.UserClock);
            return;
        }

        if (rates.Count == 0)
            Theme.Empty("No pay rates for this venue yet. Add them in the website admin (Pay rates), then Refresh.", FontAwesomeIcon.Coins);
        else
            DrawAddForm(source, rates, shifts);

        DrawOnShift(shifts);
        DrawOwed(shifts);
        DrawFilters(shifts);
        DrawTable(shifts.Where(s => Matches(s, filter)
                                    && (search.Length == 0 || s.StaffName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                           || s.Role.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList());

        DrawEditPopup(source, rates);
    }

    // ---- Record a shift -----------------------------------------------------------------------

    private void DrawAddForm(ShiftList source, IReadOnlyList<PayRateDto> rates, IReadOnlyList<ShiftDto> shifts)
    {
        if (!rates.Any(r => r.Name == newRole))
            newRole = rates[0].Name;
        var rate = rates.First(r => r.Name == newRole).Rate;

        var start = DateInput.Parse(newIn);
        var openEnded = newOut.Trim().Length == 0;
        var end = openEnded ? null : DateInput.Parse(newOut);
        var minutes = start is { } s && end is { } e ? (int)Math.Round((e - s).TotalMinutes) : 0;
        var alreadyOn = openEnded && shifts.Any(x => x.OnShift && x.StaffName.Equals(newName.Trim(), StringComparison.OrdinalIgnoreCase));
        var problem = start == null ? $"Type In as {DateInput.Format} (not in the future)."
            : !openEnded && end == null ? $"Type Out as {DateInput.Format}, or leave it empty if they're still here."
            : !openEnded && minutes <= 0 ? "The shift has to end after it starts."
            : minutes > 24 * 60 ? "A shift can be 24 hours at most."
            : alreadyOn ? $"{newName.Trim()} is already on shift."
            : null;

        var style = ImGui.GetStyle();
        var height = ImGui.GetTextLineHeightWithSpacing() * 3 + ImGui.GetFrameHeightWithSpacing() + style.CellPadding.Y * 4 + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##addShift", new Vector2(0, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Gold);
        Theme.SectionLabel("Record a shift");

        var button = 120 * ImGuiHelpers.GlobalScale;
        var time = 175 * ImGuiHelpers.GlobalScale;
        using (var form = ImRaii.Table("##shiftForm", 6, ImGuiTableFlags.SizingFixedFit))
        {
            if (form.Success)
            {
                ImGui.TableSetupColumn("Who", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthFixed, time);
                ImGui.TableSetupColumn("Out", ImGuiTableColumnFlags.WidthFixed, time);
                ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 0.8f);
                ImGui.TableSetupColumn("Add", ImGuiTableColumnFlags.WidthFixed, button);

                ImGui.TableNextRow();
                foreach (var label in new[] { "Who", "Role", "In (your time)", "Out (empty = still here)", "Note", "" })
                {
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(label);
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                NameField("##shiftName", ref newName);

                ImGui.TableNextColumn();
                RoleCombo("##shiftRole", ref newRole, rates);

                ImGui.TableNextColumn();
                DateInput.Draw("##shiftIn", ref newIn, start);

                ImGui.TableNextColumn();
                OutField("##shiftOut", ref newOut, openEnded || end != null);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##shiftNote", "Optional", ref newNote, 280);

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(source.Saving || newName.Trim().Length == 0 || problem != null))
                {
                    using var colors = Theme.Primary();
                    if (ImGuiComponents.IconButtonWithText(openEnded ? FontAwesomeIcon.Play : FontAwesomeIcon.Plus, openEnded ? "Start shift" : "Add shift", new Vector2(button, 0))
                        && start is { } from)
                    {
                        source.Add(newName.Trim(), newRole, from, end, newNote.Trim());
                        newName = newNote = newOut = string.Empty;
                        newIn = DateInput.Now();
                    }
                }
            }
        }

        if (problem != null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red))
                ImGui.Text(problem);
        }
        else if (openEnded)
        {
            ImGui.TextDisabled($"{newRole} at {plugin.FormatGil(rate)}/h. They show under On shift now until you end it.");
        }
        else
        {
            ImGui.TextDisabled($"{Hours(minutes)} worked = {PaidHoursText(ShiftList.PaidHours(minutes))} as {newRole} at {plugin.FormatGil(rate)}/h =");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                ImGui.Text(plugin.FormatGil(ShiftList.Pay(rate, minutes)));
        }
    }

    /// <summary>A name box with a dropdown of the venue's staff to pick from.</summary>
    private void NameField(string id, ref string name)
    {
        var arrow = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - arrow - ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.InputTextWithHint(id, "Character name", ref name, 64);
        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        using var combo = ImRaii.Combo($"{id}pick", string.Empty, ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft);
        if (!combo.Success)
            return;
        var venue = plugin.Configuration.VenueSlug;
        var staff = plugin.Configuration.Staff.Where(s => s.Venue == venue).OrderByDescending(s => s.OnShift).ThenBy(s => s.Name).ToList();
        if (staff.Count == 0)
            ImGui.TextDisabled("No staff synced for this venue.");
        foreach (var member in staff)
        {
            if (ImGui.Selectable($"{member.Name}{(member.Role.Length > 0 ? $"  ({member.Role})" : "")}##{member.Name}", member.Name == name))
                name = member.Name;
        }
    }

    private void RoleCombo(string id, ref string role, IReadOnlyList<PayRateDto> rates, float width = -1)
    {
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo(id, role);
        if (!combo.Success)
            return;
        foreach (var option in rates)
        {
            if (ImGui.Selectable($"{option.Name}  ({plugin.FormatGil(option.Rate)}/h)##{option.Id}", option.Name == role))
                role = option.Name;
        }
    }

    /// <summary>Out time: empty means still on shift.</summary>
    private static void OutField(string id, ref string text, bool valid, float width = -1)
    {
        var now = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth((width < 0 ? ImGui.GetContentRegionAvail().X : width) - now - spacing);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red, !valid))
            ImGui.InputTextWithHint(id, "still here", ref text, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"When they left, as {DateInput.Format} (your time). Leave empty if they're still on shift.");
        ImGui.SameLine();
        if (ImGuiComponents.IconButton($"{id}now", FontAwesomeIcon.Clock))
            text = DateInput.Now();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Now");
    }

    // ---- On shift now and owed ----------------------------------------------------------------

    private void DrawOnShift(IReadOnlyList<ShiftDto> shifts)
    {
        var on = shifts.Where(s => s.OnShift).OrderBy(s => s.StartedAt).ToList();
        if (on.Count == 0)
            return;

        var style = ImGui.GetStyle();
        var height = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeightWithSpacing() * on.Count + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##onShift", new Vector2(0, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Green);
        Theme.SectionLabel($"On shift now ({on.Count})");

        var endWidth = 110 * ImGuiHelpers.GlobalScale;
        foreach (var shift in on)
        {
            using var id = ImRaii.PushId(shift.Id);
            ImGui.AlignTextToFramePadding();
            Theme.Dot(Theme.Green);
            ImGui.SameLine();
            ImGui.Text(shift.StaffName);
            ImGui.SameLine();
            ImGui.TextDisabled($"{shift.Role} · since {shift.StartedAt.ToLocalTime():t} · {Hours(shift.MinutesSoFar)} so far, {PaidHoursText(shift.PaidHours)} ·");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                ImGui.Text(plugin.FormatGil(ShiftList.Pay(shift.Rate, shift.MinutesSoFar)));
            ImGui.SameLine();
            ImGui.TextDisabled($"· next hour counts at {shift.NextPaidHourAt.ToLocalTime():t}");

            ImGui.SameLine(ImGui.GetContentRegionMax().X - endWidth);
            using (ImRaii.Disabled(plugin.Shifts.Saving))
            using (Theme.Primary())
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, "End shift", new Vector2(endWidth, 0)))
                    plugin.Shifts.EndNow(shift);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{shift.StaffName} leaves now.");
        }
    }

    /// <summary>What each person is still owed: ended shifts not marked paid.</summary>
    private void DrawOwed(IReadOnlyList<ShiftDto> shifts)
    {
        var owed = shifts.Where(s => !s.OnShift && !s.Paid)
            .GroupBy(s => s.StaffName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().StaffName, Shifts: g.ToList(), Total: g.Sum(s => s.Pay), Hours: g.Sum(s => s.PaidHours)))
            .OrderByDescending(o => o.Total)
            .ToList();

        var style = ImGui.GetStyle();
        var rows = Math.Max(owed.Count, 1) + (owed.Count > 1 ? 1 : 0);
        var height = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeightWithSpacing() * rows + style.WindowPadding.Y * 2;
        using var box = Theme.Card("##payOwed", new Vector2(0, height));
        if (!box.Success)
            return;
        Theme.CardAccent(owed.Count == 0 ? Theme.Green : Theme.Gold);
        Theme.SectionLabel("Pay owed to staff");

        if (owed.Count == 0)
        {
            Theme.Icon(FontAwesomeIcon.CheckCircle, Theme.Green);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Green))
                ImGui.Text("Everyone has been paid for their shifts.");
            return;
        }

        var button = 90 * ImGuiHelpers.GlobalScale;
        var amountRight = ImGui.GetContentRegionMax().X - button - style.ItemSpacing.X;
        foreach (var (name, list, total, hours) in owed)
        {
            using var id = ImRaii.PushId(name);
            ImGui.AlignTextToFramePadding();
            Theme.Dot(Theme.Gold);
            ImGui.SameLine();
            ImGui.Text(name);
            ImGui.SameLine();
            ImGui.TextDisabled($"{(list.Count == 1 ? "1 shift" : $"{list.Count} shifts")}, {PaidHoursText(hours)}");
            var amount = plugin.FormatGil(total);
            ImGui.SameLine(amountRight - ImGui.CalcTextSize(amount).X);
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                ImGui.Text(amount);

            ImGui.SameLine(ImGui.GetContentRegionMax().X - button);
            var ctrl = ImGui.GetIO().KeyCtrl;
            using (ImRaii.Disabled(!ctrl || plugin.Shifts.Saving))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Paid", new Vector2(button, 0)))
                {
                    foreach (var shift in list)
                        plugin.Shifts.SetPaid(shift, true);
                }
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(ctrl ? $"Mark all of {name}'s {list.Count} unpaid shifts paid ({amount})." : "Hold Ctrl and click to mark all their shifts paid.");
        }

        if (owed.Count > 1)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Total");
            var all = plugin.FormatGil(owed.Sum(o => o.Total));
            ImGui.SameLine(amountRight - ImGui.CalcTextSize(all).X);
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                ImGui.Text(all);
        }
    }

    // ---- Shifts table -------------------------------------------------------------------------

    private void DrawFilters(IReadOnlyList<ShiftDto> shifts)
    {
        foreach (var (key, label) in Filters)
        {
            if (Theme.Chip($"{label} ({shifts.Count(s => Matches(s, key))})", filter == key))
                filter = key;
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(Math.Max(120 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##shiftSearch", "Search names or roles...", ref search, 64);
    }

    private void DrawTable(List<ShiftDto> shifts)
    {
        if (shifts.Count == 0)
        {
            Theme.Empty(filter == "unpaid" ? "No unpaid shifts." : "No shifts here.", FontAwesomeIcon.UserClock);
            return;
        }

        using var table = ImRaii.Table("##shifts", 10, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable
                                                     | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.WidthFixed, 95 * scale);
        ImGui.TableSetupColumn("Who", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthFixed, 65 * scale);
        ImGui.TableSetupColumn("Out", ImGuiTableColumnFlags.WidthFixed, 75 * scale);
        ImGui.TableSetupColumn("Hours", ImGuiTableColumnFlags.WidthFixed, 65 * scale);
        ImGui.TableSetupColumn("Rate", ImGuiTableColumnFlags.WidthFixed, 100 * scale);
        ImGui.TableSetupColumn("Pay", ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn("Paid", ImGuiTableColumnFlags.WidthFixed, 40 * scale);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 70 * scale);
        ImGui.TableHeadersRow();

        foreach (var shift in shifts)
        {
            using var id = ImRaii.PushId(shift.Id);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
            if (shift.OnShift)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(Theme.Green with { W = 0.07f }));

            var start = shift.StartedAt.ToLocalTime();
            Cell(start.ToString("ddd MMM d"));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(shift.StaffName);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Recorded by {shift.RecordedBy}{(shift.Note.Length > 0 ? $"\n{shift.Note}" : "")}");

            Cell(shift.Role);
            Cell(start.ToString("t"));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (shift.EndedAt is { } end)
            {
                // Next day's time gets a +1 so overnight shifts read right.
                var local = end.ToLocalTime();
                ImGui.Text(local.Date > start.Date ? $"{local:t} +1" : local.ToString("t"));
            }
            else
            {
                Theme.Pill("on shift", Theme.Green);
            }

            // Paid hours; the time really worked on hover.
            Cell(shift.PaidHours == 1 ? "1 hour" : $"{shift.PaidHours} hours");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Worked {Hours(shift.MinutesSoFar)}{(shift.OnShift ? " so far" : "")}. {ShiftList.CountsAsHour} minutes or more of a part hour counts as an hour.");
            Cell($"{plugin.FormatGil(shift.Rate)}/h", disabled: true);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, shift.Paid ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : Theme.Gold))
                ImGui.Text(plugin.FormatGil(shift.OnShift ? ShiftList.Pay(shift.Rate, shift.MinutesSoFar) : shift.Pay));
            if (shift.OnShift && ImGui.IsItemHovered())
                ImGui.SetTooltip("So far; worked out when the shift ends.");

            ImGui.TableNextColumn();
            var paid = shift.Paid;
            using (ImRaii.Disabled(shift.OnShift))
            {
                if (ImGui.Checkbox("##paid", ref paid))
                    plugin.Shifts.SetPaid(shift, paid);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(shift.OnShift ? "End the shift first." : "Tick when you've paid them for this shift.");

            ImGui.TableNextColumn();
            DrawActions(shift);
        }
    }

    private static void Cell(string text, bool disabled = false)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (disabled)
            ImGui.TextDisabled(text);
        else
            ImGui.Text(text);
    }

    private void DrawActions(ShiftDto shift)
    {
        using var disabled = ImRaii.Disabled(plugin.Shifts.Saving);
        if (ImGuiComponents.IconButton("edit", FontAwesomeIcon.Pen))
            OpenEdit(shift);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fix who, the role, the times or the note.");

        ImGui.SameLine();
        var ctrl = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrl))
        {
            if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash))
                plugin.Shifts.Remove(shift);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ctrl ? "Delete this shift (recorded by mistake)." : "Hold Ctrl and click to delete.");
    }

    // ---- Edit ---------------------------------------------------------------------------------

    private void OpenEdit(ShiftDto shift)
    {
        editing = shift;
        editName = shift.StaffName;
        editRole = shift.Role;
        editIn = DateInput.Text(shift.StartedAt);
        editOut = shift.EndedAt is { } end ? DateInput.Text(end) : string.Empty;
        editNote = shift.Note;
        openEdit = true;
    }

    private void DrawEditPopup(ShiftList source, IReadOnlyList<PayRateDto> rates)
    {
        if (openEdit)
        {
            ImGui.OpenPopup(EditPopup);
            openEdit = false;
        }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        using var popup = ImRaii.PopupModal(EditPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        if (!popup.Success || editing is not { } shift)
            return;

        using (Theme.TitleFont())
            ImGui.Text($"Edit {shift.StaffName}'s shift");
        ImGui.TextDisabled(shift.Paid ? "Already marked paid." : "Changing the role uses that role's current rate.");
        ImGuiHelpers.ScaledDummy(4);

        var width = 280 * ImGuiHelpers.GlobalScale;
        ImGui.TextDisabled("Who");
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##editName", ref editName, 64);
        ImGui.TextDisabled("Role");
        RoleCombo("##editRole", ref editRole, rates, width);
        ImGui.TextDisabled("In (your time)");
        var start = DateInput.Parse(editIn);
        DateInput.Draw("##editIn", ref editIn, start, width);
        ImGui.TextDisabled("Out (empty = still on shift)");
        var openEnded = editOut.Trim().Length == 0;
        var end = openEnded ? null : DateInput.Parse(editOut);
        OutField("##editOut", ref editOut, openEnded || end != null, width);
        ImGui.TextDisabled("Note");
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##editNote", ref editNote, 280);

        var minutes = start is { } s && end is { } e ? (int)Math.Round((e - s).TotalMinutes) : 0;
        var valid = start != null && editName.Trim().Length > 0 && (openEnded || (end != null && minutes > 0 && minutes <= 24 * 60))
                    && !(openEnded && shift.Paid);
        if (!valid && start != null && end != null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red))
                ImGui.Text(minutes <= 0 ? "The shift has to end after it starts." : "A shift can be 24 hours at most.");
        }
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (ImRaii.Disabled(!valid))
        using (Theme.Primary())
        {
            if (ImGui.Button("Save", button) && start is { } from)
            {
                source.Update(shift, editName.Trim(), editRole, from, end, editNote.Trim());
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    private static string Hours(int minutes) => $"{minutes / 60}h{minutes % 60:00}";

    private static string PaidHoursText(int hours) => hours == 1 ? "1 paid hour" : $"{hours} paid hours";

    private static bool Matches(ShiftDto shift, string key) => key switch
    {
        "paid" => shift.Paid,
        "all" => true,
        _ => !shift.Paid,
    };
}
