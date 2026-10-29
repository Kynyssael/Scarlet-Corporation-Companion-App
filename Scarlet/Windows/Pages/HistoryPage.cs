using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// Management only: every bartender's nights at the selected venue, from the website.
/// Tick Paid when a bartender hands over the gil; the website admin shows the same.
/// </summary>
public class HistoryPage(Plugin plugin)
{
    private static readonly Vector4 Rose = new(1f, 0.62f, 0.66f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);

    private static readonly (string Key, string Label)[] Filters =
        [("unpaid", "Not paid"), ("paid", "Paid"), ("voided", "Voided"), ("all", "Everything")];

    private string filter = "unpaid";

    public void Draw()
    {
        var source = plugin.VenueNights;
        source.RefreshIfStale();

        DrawHeader(source);

        if (source.Error != null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.45f, 1f)))
                ImGui.TextWrapped(source.Error);
        }

        if (source.Nights is not { } nights)
        {
            ImGui.TextDisabled(source.Loading ? "Loading nights from the website..." : "Nothing loaded yet.");
            return;
        }

        DrawOwed(nights);
        DrawFilters(nights);
        DrawTable(nights.Where(n => Matches(n, filter)).OrderByDescending(n => n.EndedAt).ToList());
    }

    private void DrawHeader(VenueNights source)
    {
        ImGui.Text($"{ScarletWebClient.VenueName(plugin.Configuration.VenueSlug)} nights");
        ImGui.SameLine();
        ImGui.TextDisabled(source.Loading ? "refreshing..." : source.UpdatedAt is { } at ? $"from the website, {at:t}" : string.Empty);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - 90 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(source.Loading))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh", ImGuiHelpers.ScaledVector2(90, 0)))
                source.Refresh();
        }
    }

    /// <summary>What each bartender still has to hand over to management: nights not paid and not voided.</summary>
    private void DrawOwed(IReadOnlyList<NightReportDto> nights)
    {
        var owed = nights.Where(n => Matches(n, "unpaid"))
            .GroupBy(n => n.Who)
            .Select(g => (Name: g.Key, Total: g.Sum(n => n.Total), Count: g.Count()))
            .OrderByDescending(o => o.Total)
            .ToList();

        using var box = ImRaii.Child("##owed", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * (Math.Max(owed.Count, 1) + 1) + ImGui.GetStyle().WindowPadding.Y * 2), true);
        if (!box.Success)
            return;

        using (ImRaii.PushColor(ImGuiCol.Text, Rose))
            ImGui.Text("Owed to the venue");
        if (owed.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Green))
                ImGui.Text("Nothing owed: every bartender has handed in their gil.");
            return;
        }

        foreach (var (name, total, count) in owed)
        {
            ImGui.Text(name);
            ImGui.SameLine();
            ImGui.TextDisabled(count == 1 ? "(1 night)" : $"({count} nights)");
            RightAligned(plugin.FormatGil(total));
        }
    }

    private void DrawFilters(IReadOnlyList<NightReportDto> nights)
    {
        foreach (var (key, label) in Filters)
        {
            var active = filter == key;
            using var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.70f, 0.07f, 0.12f, 1f), active);
            if (ImGui.Button($"{label} ({nights.Count(n => Matches(n, key))})"))
                filter = key;
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    private void DrawTable(List<NightReportDto> nights)
    {
        if (nights.Count == 0)
        {
            ImGui.TextDisabled("Nothing here.");
            return;
        }

        using var table = ImRaii.Table("##history", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable);
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Night", ImGuiTableColumnFlags.WidthFixed, 130 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Bartender");
        ImGui.TableSetupColumn("Sold", ImGuiTableColumnFlags.WidthFixed, 70 * ImGuiHelpers.GlobalScale);
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
            ImGui.Text($"{night.ItemCount} items");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", night.Lines.Select(l => $"{l.Quantity}x {l.Name}  {plugin.FormatGil(l.UnitPrice * l.Quantity)}")));

            ImGui.TableNextColumn();
            ImGui.Text(plugin.FormatGil(night.Total));

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

    private static void RightAligned(string text)
    {
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(text).X);
        ImGui.Text(text);
    }
}
