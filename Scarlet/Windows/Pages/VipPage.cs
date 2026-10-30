using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// The venue's VIP members (monthly, yearly, lifetime), when each bought it and when they need to
/// renew. Every staff member sees it; management adds, renews and edits. Kept on the website.
/// </summary>
public class VipPage(Plugin plugin)
{
    private const string RenewPopup = "Renew VIP##scarletRenewVip";
    private const string EditPopup = "Edit VIP##scarletEditVip";

    private static readonly (string Key, string Label)[] Filters =
        [("attention", "Needs renewing"), ("active", "Active"), ("lifetime", "Lifetime"), ("all", "Everyone")];

    private string filter = "all";
    private string search = string.Empty;

    // The "add a VIP" form.
    private string newName = string.Empty;
    private string newTier = VipList.Monthly;
    private string newBought = DateInput.Now();
    private string newNote = string.Empty;

    // The renew and edit popups.
    private VipDto? editing;
    private string editName = string.Empty;
    private string editTier = VipList.Monthly;
    private string editDate = string.Empty;
    private string editNote = string.Empty;
    private string? openPopup;

    /// <summary>VIPs expired or running out within a week, for the count on the tab.</summary>
    public int NeedRenewing => plugin.Vips.Vips?.Count(v => v.Expired || v.DueSoon) ?? 0;

    public void Draw()
    {
        var source = plugin.Vips;
        source.RefreshIfStale();
        var manager = plugin.Auth.CurrentAccess.Manage;

        var refresh = 90 * ImGuiHelpers.GlobalScale;
        Theme.PageHeader($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} VIPs",
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
        else
            ImGui.TextDisabled("Who's a VIP at this venue, and until when. Management adds and renews VIPs.");

        if (source.Vips is not { } vips)
        {
            Theme.Empty(source.Loading ? "Loading VIPs from the website..." : "Nothing loaded yet.", FontAwesomeIcon.Crown);
            return;
        }

        DrawFilters(vips);
        DrawTable(vips.Where(v => Matches(v, filter)
                                  && (search.Length == 0 || v.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                         || v.Note.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList(), manager);

        if (manager)
        {
            DrawRenewPopup(source);
            DrawEditPopup(source);
        }
    }

    // ---- Add ----------------------------------------------------------------------------------

    private void DrawAddForm(VipList source)
    {
        var style = ImGui.GetStyle();
        var bought = DateInput.Parse(newBought);
        var height = ImGui.GetTextLineHeightWithSpacing() * 3 + ImGui.GetFrameHeightWithSpacing() + style.CellPadding.Y * 4 + style.WindowPadding.Y * 2;
        using var card = Theme.Card("##addVip", new Vector2(0, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Gold);
        Theme.SectionLabel("Add a VIP");

        var button = 110 * ImGuiHelpers.GlobalScale;
        using (var form = ImRaii.Table("##addForm", 5, ImGuiTableFlags.SizingFixedFit))
        {
            if (form.Success)
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Bought", ImGuiTableColumnFlags.WidthFixed, 190 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Add", ImGuiTableColumnFlags.WidthFixed, button);

                ImGui.TableNextRow();
                foreach (var label in new[] { "Name", "VIP", "Bought (your time)", "Note", "" })
                {
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(label);
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##vipName", "Character name", ref newName, 64);

                ImGui.TableNextColumn();
                TierCombo("##vipTier", ref newTier);

                ImGui.TableNextColumn();
                DateInput.Draw("##vipBought", ref newBought, bought);

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##vipNote", "Optional", ref newNote, 280);

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(source.Saving || newName.Trim().Length == 0 || bought == null))
                {
                    using var colors = Theme.Primary();
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crown, "Add", new Vector2(button, 0)) && bought is { } at)
                    {
                        source.Add(newName.Trim(), newTier, at, newNote.Trim());
                        newName = newNote = string.Empty;
                        newBought = DateInput.Now();
                    }
                }
            }
        }

        // What that means for renewing.
        if (bought is { } when)
        {
            var renews = VipList.ExpiryFrom(newTier, when);
            ImGui.TextDisabled(renews is { } date ? $"{VipList.TierLabel(newTier)} VIP bought {when:MMM d, yyyy}: renews on" : "Lifetime VIP: never needs renewing.");
            if (renews is { } due)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                    ImGui.Text(due.ToString("ddd MMM d, yyyy"));
            }
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red))
                ImGui.Text($"Type the date as {DateInput.Format}, e.g. {DateInput.Now()} (not in the future).");
        }
    }

    // ---- List ---------------------------------------------------------------------------------

    private void DrawFilters(IReadOnlyList<VipDto> vips)
    {
        foreach (var (key, label) in Filters)
        {
            var count = vips.Count(v => Matches(v, key));
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Amber, key == "attention" && count > 0 && filter != key))
            {
                if (Theme.Chip($"{label} ({count})", filter == key))
                    filter = key;
            }
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(Math.Max(120 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##vipSearch", "Search names or notes...", ref search, 64);
    }

    private void DrawTable(List<VipDto> vips, bool manager)
    {
        if (vips.Count == 0)
        {
            Theme.Empty(filter == "attention" ? "Nobody needs to renew this week." : "No VIPs here yet.", FontAwesomeIcon.Crown);
            return;
        }

        using var table = ImRaii.Table("##vips", manager ? 7 : 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable
                                                  | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("VIP", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Bought", ImGuiTableColumnFlags.WidthFixed, 140 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Renews", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 130 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 1f);
        if (manager)
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        foreach (var vip in vips)
        {
            using var id = ImRaii.PushId(vip.Id);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
            if (vip.Expired || vip.DueSoon)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32((vip.Expired ? Theme.Red : Theme.Amber) with { W = 0.08f }));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(vip.Name);
            if (ImGui.IsItemHovered())
                DrawHistory(vip);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Theme.Pill(VipList.TierLabel(vip.Tier), vip.Tier switch
            {
                VipList.Lifetime => Theme.Gold,
                VipList.Yearly => Theme.Rose,
                _ => Theme.Muted,
            });

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(vip.BoughtAt.ToLocalTime().ToString("MMM d, yyyy"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(vip.BoughtAt.ToLocalTime().ToString("dddd MMM d, yyyy 'at' t"));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (vip.ExpiresAt is { } expires)
                ImGui.Text(expires.ToLocalTime().ToString("MMM d, yyyy"));
            else
                ImGui.TextDisabled("never");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            DrawStatus(vip);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(vip.Note);
            if (vip.Note.Length > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip(vip.Note);

            if (manager)
            {
                ImGui.TableNextColumn();
                DrawActions(vip);
            }
        }
    }

    private static void DrawStatus(VipDto vip)
    {
        switch (vip.DaysLeft)
        {
            case null:
                Theme.Pill("Lifetime", Theme.Gold);
                break;
            case var _ when vip.Expired:
                var ago = -vip.DaysLeft!.Value;
                Theme.Pill(ago == 0 ? "Expired today" : ago == 1 ? "Expired yesterday" : $"Expired {ago} days ago", Theme.Red);
                break;
            case var days when vip.DueSoon:
                Theme.Pill(days <= 1 ? "Renews tomorrow" : $"Renews in {days} days", Theme.Amber);
                break;
            case var days:
                Theme.Pill($"{days} days left", Theme.Green);
                break;
        }
    }

    /// <summary>Every purchase and renewal, newest first.</summary>
    private void DrawHistory(VipDto vip)
    {
        using var tooltip = ImRaii.Tooltip();
        Theme.SectionLabel("Purchases");
        foreach (var p in vip.Purchases)
        {
            ImGui.Text($"{p.BoughtAt.ToLocalTime():MMM d, yyyy}  {VipList.TierLabel(p.Tier)}");
            ImGui.SameLine();
            ImGui.TextDisabled(p.ExpiresAt is { } until ? $"until {until.ToLocalTime():MMM d, yyyy}" : "for life");
            if (p.SoldBy.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"· by {p.SoldBy}");
            }
        }
        if (vip.Purchases.Count == 0)
            ImGui.TextDisabled("None recorded.");
    }

    private void DrawActions(VipDto vip)
    {
        using var disabled = ImRaii.Disabled(plugin.Vips.Saving);
        using (Theme.Primary(vip.Expired || vip.DueSoon))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Redo, "Renew", ImGuiHelpers.ScaledVector2(80, 0)))
                Open(RenewPopup, vip);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("They paid again: record the renewal.");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("edit", FontAwesomeIcon.Pen))
            Open(EditPopup, vip);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fix the name, VIP type, purchase date or note.");

        ImGui.SameLine();
        var ctrl = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrl))
        {
            if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash))
                plugin.Vips.Remove(vip);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ctrl ? $"Remove {vip.Name} and their purchase history." : "Hold Ctrl and click to remove.");
    }

    private void Open(string popup, VipDto vip)
    {
        editing = vip;
        editName = vip.Name;
        editTier = vip.Tier;
        editNote = vip.Note;
        // Renewing: paid now. Editing: the purchase date as it is.
        editDate = popup == RenewPopup ? DateInput.Now() : DateInput.Text(vip.BoughtAt);
        openPopup = popup;
    }

    // ---- Popups -------------------------------------------------------------------------------

    private void DrawRenewPopup(VipList source)
    {
        if (!BeginPopup(RenewPopup) || editing is not { } vip)
            return;
        using var end = new PopupEnd();

        using (Theme.TitleFont())
            ImGui.Text($"Renew {vip.Name}");
        ImGui.TextDisabled(vip.ExpiresAt is { } current
            ? $"{VipList.TierLabel(vip.Tier)} VIP, {(vip.Expired ? "ran out" : "runs out")} {current.ToLocalTime():ddd MMM d, yyyy}"
            : "Lifetime VIP");
        ImGuiHelpers.ScaledDummy(4);

        ImGui.TextDisabled("Renew as");
        TierCombo("##renewTier", ref editTier, 160 * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled("Paid (your time)");
        var paid = DateInput.Parse(editDate);
        DateInput.Draw("##renewPaid", ref editDate, paid, 160 * ImGuiHelpers.GlobalScale);

        if (paid is { } at)
        {
            var until = VipList.RenewedExpiry(vip, editTier, at);
            ImGui.Text(until is { } u ? "New renewal date:" : "Lifetime: never needs renewing again.");
            if (until is { } date)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
                    ImGui.Text(date.ToLocalTime().ToString("ddd MMM d, yyyy"));
                if (vip.ExpiresAt is { } running && running > at)
                    ImGui.TextDisabled("Renewed early, so the new time starts when the current one ends.");
            }
        }
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (ImRaii.Disabled(paid == null))
        using (Theme.Primary())
        {
            if (ImGui.Button("Renew", button) && paid is { } when)
            {
                source.Renew(vip, editTier, when);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    private void DrawEditPopup(VipList source)
    {
        if (!BeginPopup(EditPopup) || editing is not { } vip)
            return;
        using var end = new PopupEnd();

        using (Theme.TitleFont())
            ImGui.Text($"Edit {vip.Name}");
        ImGui.TextDisabled("Changing the VIP type or date works the renewal date out again from it.");
        ImGuiHelpers.ScaledDummy(4);

        var width = 260 * ImGuiHelpers.GlobalScale;
        ImGui.TextDisabled("Name");
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##editName", ref editName, 64);
        ImGui.TextDisabled("VIP");
        TierCombo("##editTier", ref editTier, width);
        ImGui.TextDisabled("Bought (your time)");
        var bought = DateInput.Parse(editDate);
        DateInput.Draw("##editBought", ref editDate, bought, width);
        ImGui.TextDisabled("Note");
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##editNote", ref editNote, 280);
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (ImRaii.Disabled(bought == null || editName.Trim().Length == 0))
        using (Theme.Primary())
        {
            if (ImGui.Button("Save", button) && bought is { } at)
            {
                source.Update(vip, editName.Trim(), editTier, at, editNote.Trim());
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    /// <summary>Opens the popup if a button asked for it, then begins it. End it with <see cref="PopupEnd"/>.</summary>
    private bool BeginPopup(string name)
    {
        if (openPopup == name)
        {
            ImGui.OpenPopup(name);
            openPopup = null;
        }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        return ImGui.BeginPopupModal(name, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
    }

    private readonly struct PopupEnd : IDisposable
    {
        public void Dispose() => ImGui.EndPopup();
    }

    // ---- Fields -------------------------------------------------------------------------------

    private static void TierCombo(string id, ref string tier, float width = -1)
    {
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo(id, VipList.TierLabel(tier));
        if (!combo.Success)
            return;
        foreach (var option in VipList.Tiers)
        {
            if (ImGui.Selectable(VipList.TierLabel(option), option == tier))
                tier = option;
        }
    }

    private static bool Matches(VipDto vip, string key) => key switch
    {
        "attention" => vip.Expired || vip.DueSoon,
        "active" => vip.ExpiresAt != null && !vip.Expired && !vip.DueSoon,
        "lifetime" => vip.ExpiresAt == null,
        _ => true,
    };
}
