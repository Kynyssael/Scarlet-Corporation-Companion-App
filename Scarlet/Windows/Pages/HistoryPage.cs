using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// Management only: every bartender's, photographer's and court's nights at the selected venue, from the
/// website. Tick Paid when they hand over the gil; the website admin shows the same.
/// </summary>
public class HistoryPage(Plugin plugin)
{
    private static readonly (string Key, string Label)[] Filters =
        [("unpaid", "Not paid"), ("paid", "Paid"), ("voided", "Voided"), ("all", "Everything")];

    private string filter = "unpaid";

    public void Draw()
    {
        var source = plugin.VenueNights;
        source.RefreshIfStale();

        DrawHeader(source);

        if (source.Error != null)
            Theme.ErrorText(source.Error);

        if (source.Nights is not { } nights)
        {
            Theme.Empty(source.Loading ? "Loading nights from the website..." : "Nothing loaded yet.", FontAwesomeIcon.Moon);
            return;
        }

        DrawOwed(nights);
        DrawFilters(nights);
        DrawTable(nights.Where(n => Matches(n, filter)).OrderByDescending(n => n.EndedAt).ToList());
    }

    private void DrawHeader(VenueNights source)
    {
        var refresh = 90 * ImGuiHelpers.GlobalScale;
        Theme.PageHeader($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} nights",
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

    /// <summary>What each person still has to hand over to management: nights not paid and not voided.</summary>
    private void DrawOwed(IReadOnlyList<NightReportDto> nights)
    {
        var owed = nights.Where(n => Matches(n, "unpaid"))
            .GroupBy(n => n.Who)
            .Select(g => (Name: g.Key, Total: g.Sum(n => n.Owed), Count: g.Count()))
            .OrderByDescending(o => o.Total)
            .ToList();

        var style = ImGui.GetStyle();
        var rows = Math.Max(owed.Count, 1) + (owed.Count > 1 ? 1 : 0);
        var height = ImGui.GetTextLineHeightWithSpacing() * (rows + 1) + style.WindowPadding.Y * 2;
        using var box = Theme.Card("##owed", new Vector2(0, height));
        if (!box.Success)
            return;
        Theme.CardAccent(owed.Count == 0 ? Theme.Green : Theme.Gold);

        Theme.SectionLabel("Owed to the venue");
        if (owed.Count == 0)
        {
            Theme.Icon(FontAwesomeIcon.CheckCircle, Theme.Green);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Green))
                ImGui.Text("Nothing owed: everyone has handed in their gil.");
            return;
        }

        foreach (var (name, total, count) in owed)
        {
            Theme.Dot(Theme.Gold);
            ImGui.SameLine();
            ImGui.Text(name);
            ImGui.SameLine();
            ImGui.TextDisabled(count == 1 ? "1 night" : $"{count} nights");
            RightAligned(plugin.FormatGil(total), Theme.Gold);
        }

        if (owed.Count > 1)
        {
            ImGui.TextDisabled("Total");
            RightAligned(plugin.FormatGil(owed.Sum(o => o.Total)), Theme.Gold);
        }
    }

    private void DrawFilters(IReadOnlyList<NightReportDto> nights)
    {
        ImGuiHelpers.ScaledDummy(2);
        foreach (var (key, label) in Filters)
        {
            if (Theme.Chip($"{label} ({nights.Count(n => Matches(n, key))})", filter == key))
                filter = key;
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    private void DrawTable(List<NightReportDto> nights)
    {
        if (nights.Count == 0)
        {
            Theme.Empty("Nothing here.", FontAwesomeIcon.Moon);
            return;
        }

        using var table = ImRaii.Table("##history", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Night", ImGuiTableColumnFlags.WidthFixed, 130 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Staff");
        ImGui.TableSetupColumn("Sold", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Owed", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Paid", ImGuiTableColumnFlags.WidthFixed, 40 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##void", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        foreach (var night in nights)
        {
            using var id = ImRaii.PushId(night.Id);
            using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.45f, night.Voided);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(night.StartedAt.ToLocalTime().ToString("ddd MMM d, t"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{night.StartedAt.ToLocalTime():g} to {night.EndedAt.ToLocalTime():t}");

            ImGui.TableNextColumn();
            ImGui.Text(night.Who);
            if (night.Bartender.Length > 0 && night.StaffName.Length > 0 && night.Bartender != night.StaffName && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Signed in as {night.StaffName}");

            ImGui.TableNextColumn();
            // A tag for what kind of night it was, then how much was sold.
            Theme.Pill(night.Kind switch { "photo" => "Photo", "court" => "Court", _ => "Bar" },
                night.Kind switch { "photo" => Theme.Rose, "court" => Theme.Gold, _ => Theme.Muted });
            ImGui.SameLine();
            ImGui.Text(night.Kind switch
            {
                "photo" => $"{night.ItemCount} photos",
                "court" => $"{night.ItemCount} sessions",
                _ => $"{night.ItemCount} items",
            });
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", night.Lines.Select(l => l.Describe(plugin.FormatGil))));

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, night.Paid || night.Voided ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : Theme.Gold))
                ImGui.Text(plugin.FormatGil(night.Owed));
            if (night.HouseCut != 100 && ImGui.IsItemHovered())
                ImGui.SetTooltip($"{night.HouseCut}% house cut of {plugin.FormatGil(night.Total)} in sales");

            ImGui.TableNextColumn();
            var paid = night.Paid;
            using (ImRaii.Disabled(night.Voided))
            {
                if (ImGui.Checkbox("##paid", ref paid))
                    plugin.VenueNights.Update(night, paid: paid);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(night.Voided ? "Restore the night to mark it paid." : "Tick when the bartender has handed the gil over.");

            ImGui.TableNextColumn();
            if (ImGui.SmallButton(night.Voided ? "Restore" : "Void"))
                plugin.VenueNights.Update(night, voided: !night.Voided);
        }
    }

    private static bool Matches(NightReportDto night, string key) => key switch
    {
        "all" => true,
        "voided" => night.Voided,
        "paid" => !night.Voided && night.Paid,
        _ => !night.Voided && !night.Paid,
    };

    private static void RightAligned(string text, Vector4 color)
    {
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(text).X);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.Text(text);
    }
}
