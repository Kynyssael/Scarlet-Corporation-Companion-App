using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// The venue's blacklist: who's banned, why, since when and until when. Management only: the tab is
/// hidden from other staff, and the website refuses to send it to them.
/// </summary>
public class BlacklistPage(Plugin plugin)
{
    private const string EditPopup = "Edit ban##scarletEditBan";

    /// <summary>How long a new ban lasts; Custom takes a typed end date.</summary>
    private static readonly (string Label, Func<DateTimeOffset, DateTimeOffset?>? Until)[] Lengths =
    [
        ("Permanent", null),
        ("1 week", d => d.AddDays(7)),
        ("1 month", d => d.AddMonths(1)),
        ("3 months", d => d.AddMonths(3)),
        ("6 months", d => d.AddMonths(6)),
        ("1 year", d => d.AddYears(1)),
        ("Until a date...", null),
    ];
    private const int Custom = 6;

    private static readonly (string Key, string Label)[] Filters =
        [("active", "Banned now"), ("ended", "Ended"), ("all", "Everyone")];

    private string filter = "active";
    private string search = string.Empty;

    // The "ban someone" form.
    private string newName = string.Empty;
    private string newReason = string.Empty;
    private string newBanned = DateInput.Now();
    private int newLength;
    private string newUntil = string.Empty;

    // The edit popup.
    private BanDto? editing;
    private string editName = string.Empty;
    private string editReason = string.Empty;
    private string editBanned = string.Empty;
    private bool editPermanent;
    private string editUntil = string.Empty;
    private bool openEdit;

    public void Draw()
    {
        var source = plugin.Bans;
        source.RefreshIfStale();
        var manager = plugin.Auth.CurrentAccess.Manage;

        var refresh = 90 * ImGuiHelpers.GlobalScale;
        Theme.PageHeader($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} blacklist",
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

        if (manager)
            DrawAddForm(source);

        if (source.Bans is not { } bans)
        {
            Theme.Empty(source.Loading ? "Loading the blacklist from the website..." : "Nothing loaded yet.", FontAwesomeIcon.Ban);
            return;
        }

        DrawFilters(bans);
        DrawTable(bans.Where(b => Matches(b, filter)
                                  && (search.Length == 0 || b.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                         || b.Reason.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList(), manager);

        if (manager)
            DrawEditPopup(source);
    }

    // ---- Add ----------------------------------------------------------------------------------

    private void DrawAddForm(BanList source)
    {
        var style = ImGui.GetStyle();
        var banned = DateInput.Parse(newBanned);
        var custom = newLength == Custom;
        var until = custom ? DateInput.Parse(newUntil, future: true) : banned is { } from ? Lengths[newLength].Until?.Invoke(from) : null;
        var valid = banned != null && (!custom || (until != null && until > banned));

        var rows = custom ? 2 : 1;
        var height = ImGui.GetTextLineHeightWithSpacing() * 3 + ImGui.GetFrameHeightWithSpacing() * rows + style.CellPadding.Y * 2 * (rows + 1) + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##addBan", new Vector2(0, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Red);
        Theme.SectionLabel("Ban someone");

        var button = 110 * ImGuiHelpers.GlobalScale;
        using (var form = ImRaii.Table("##banForm", 5, ImGuiTableFlags.SizingFixedFit))
        {
            if (form.Success)
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthStretch, 1.4f);
                ImGui.TableSetupColumn("Since", ImGuiTableColumnFlags.WidthFixed, 190 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("For", ImGuiTableColumnFlags.WidthFixed, 140 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Add", ImGuiTableColumnFlags.WidthFixed, button);

                ImGui.TableNextRow();
                foreach (var label in new[] { "Name", "Reason", "Banned (your time)", "For", "" })
                {
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(label);
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##banName", "Character name", ref newName, 64);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##banReason", "What happened", ref newReason, 500);

                ImGui.TableNextColumn();
                DateInput.Draw("##banSince", ref newBanned, banned);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                using (var combo = ImRaii.Combo("##banLength", Lengths[newLength].Label))
                {
                    if (combo.Success)
                    {
                        for (var i = 0; i < Lengths.Length; i++)
                        {
                            if (ImGui.Selectable(Lengths[i].Label, i == newLength))
                            {
                                newLength = i;
                                if (i == Custom && newUntil.Length == 0)
                                    newUntil = DateInput.Text(DateTimeOffset.Now.AddMonths(1));
                            }
                        }
                    }
                }

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(source.Saving || newName.Trim().Length == 0 || !valid))
                {
                    using var colors = Theme.Primary();
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Ban, "Ban", new Vector2(button, 0)) && banned is { } at)
                    {
                        source.Add(newName.Trim(), newReason.Trim(), at, until);
                        newName = newReason = string.Empty;
                        newBanned = DateInput.Now();
                    }
                }

                if (custom)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(2);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled("Ends");
                    ImGui.TableNextColumn();
                    DateInput.Draw("##banUntil", ref newUntil, until);
                }
            }
        }

        if (!valid)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red))
                ImGui.Text(banned == null
                    ? $"Type the date as {DateInput.Format}, e.g. {DateInput.Now()} (not in the future)."
                    : "The ban has to end after it starts.");
        }
        else
        {
            ImGui.TextDisabled(until is { } end ? "Banned until" : "Banned for good.");
            if (until is { } date)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                    ImGui.Text(date.ToString("ddd MMM d, yyyy 'at' t"));
            }
        }
    }

    // ---- List ---------------------------------------------------------------------------------

    private void DrawFilters(IReadOnlyList<BanDto> bans)
    {
        foreach (var (key, label) in Filters)
        {
            if (Theme.Chip($"{label} ({bans.Count(b => Matches(b, key))})", filter == key))
                filter = key;
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(Math.Max(120 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##banSearch", "Search names or reasons...", ref search, 64);
    }

    private void DrawTable(List<BanDto> bans, bool manager)
    {
        if (bans.Count == 0)
        {
            Theme.Empty(filter == "active" ? "Nobody is banned right now." : "Nobody here.", FontAwesomeIcon.Ban);
            return;
        }

        using var table = ImRaii.Table("##bans", manager ? 6 : 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable
                                                                | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("Banned", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Until", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 130 * ImGuiHelpers.GlobalScale);
        if (manager)
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        foreach (var ban in bans)
        {
            using var id = ImRaii.PushId(ban.Id);
            using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, ban.Ended);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
            if (!ban.Ended)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(Theme.Red with { W = 0.06f }));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(ban.Name);
            if (ban.AddedBy.Length > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Banned by {ban.AddedBy}");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (ban.Reason.Length > 0)
                ImGui.Text(ban.Reason);
            else
                ImGui.TextDisabled("-");
            if (ban.Reason.Length > 0 && ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28);
                ImGui.Text(ban.Reason);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(ban.BannedAt.ToLocalTime().ToString("MMM d, yyyy"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(ban.BannedAt.ToLocalTime().ToString("dddd MMM d, yyyy 'at' t"));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (ban.Until is { } until)
            {
                ImGui.Text(until.ToLocalTime().ToString("MMM d, yyyy"));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(until.ToLocalTime().ToString("dddd MMM d, yyyy 'at' t"));
            }
            else
            {
                ImGui.TextDisabled("for good");
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (ban.Permanent)
                Theme.Pill("Permanent", Theme.Red);
            else if (ban.Ended)
                Theme.Pill("Ended", Theme.Muted);
            else
                Theme.Pill(ban.DaysLeft <= 1 ? "Ends tomorrow" : $"{ban.DaysLeft} days left", Theme.Amber);

            if (manager)
            {
                ImGui.TableNextColumn();
                dim.Pop();
                DrawActions(ban);
            }
        }
    }

    private void DrawActions(BanDto ban)
    {
        using var disabled = ImRaii.Disabled(plugin.Bans.Saving);
        if (ImGuiComponents.IconButton("edit", FontAwesomeIcon.Pen))
            OpenEdit(ban);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Change the name, reason or dates.");

        if (!ban.Ended)
        {
            ImGui.SameLine();
            var ctrl = ImGui.GetIO().KeyCtrl;
            using (ImRaii.Disabled(!ctrl))
            {
                if (ImGuiComponents.IconButton("lift", FontAwesomeIcon.Unlock))
                    plugin.Bans.EndNow(ban);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(ctrl ? $"Lift {ban.Name}'s ban now (it stays on the list as ended)." : "Hold Ctrl and click to lift the ban now.");
        }

        ImGui.SameLine();
        var remove = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!remove))
        {
            if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash))
                plugin.Bans.Remove(ban);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(remove ? $"Delete {ban.Name} from the blacklist entirely (added by mistake)." : "Hold Ctrl and click to delete.");
    }

    // ---- Edit ---------------------------------------------------------------------------------

    private void OpenEdit(BanDto ban)
    {
        editing = ban;
        editName = ban.Name;
        editReason = ban.Reason;
        editBanned = DateInput.Text(ban.BannedAt);
        editPermanent = ban.Permanent;
        editUntil = DateInput.Text(ban.Until ?? DateTimeOffset.Now.AddMonths(1));
        openEdit = true;
    }

    private void DrawEditPopup(BanList source)
    {
        if (openEdit)
        {
            ImGui.OpenPopup(EditPopup);
            openEdit = false;
        }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        using var popup = ImRaii.PopupModal(EditPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        if (!popup.Success || editing is not { } ban)
            return;

        using (Theme.TitleFont())
            ImGui.Text($"Edit {ban.Name}'s ban");
        ImGuiHelpers.ScaledDummy(4);

        var width = 280 * ImGuiHelpers.GlobalScale;
        ImGui.TextDisabled("Name");
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##editName", ref editName, 64);
        ImGui.TextDisabled("Reason");
        ImGui.InputTextMultiline("##editReason", ref editReason, 500, new Vector2(width, ImGui.GetTextLineHeight() * 3.5f));
        ImGui.TextDisabled("Banned (your time)");
        var banned = DateInput.Parse(editBanned);
        DateInput.Draw("##editBanned", ref editBanned, banned, width);
        ImGui.Checkbox("Permanent", ref editPermanent);
        DateTimeOffset? until = null;
        if (!editPermanent)
        {
            ImGui.TextDisabled("Ends (your time)");
            until = DateInput.Parse(editUntil, future: true);
            DateInput.Draw("##editUntil", ref editUntil, until, width);
        }
        var valid = banned != null && editName.Trim().Length > 0 && (editPermanent || (until != null && until > banned));
        if (banned != null && !editPermanent && until != null && until <= banned)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red))
                ImGui.Text("The ban has to end after it starts.");
        }
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (ImRaii.Disabled(!valid))
        using (Theme.Primary())
        {
            if (ImGui.Button("Save", button) && banned is { } at)
            {
                source.Update(ban, editName.Trim(), editReason.Trim(), at, editPermanent ? null : until);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    private static bool Matches(BanDto ban, string key) => key switch
    {
        "active" => !ban.Ended,
        "ended" => ban.Ended,
        _ => true,
    };
}
