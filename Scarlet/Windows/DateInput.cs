using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace Scarlet.Windows;

/// <summary>A date and time typed as yyyy-MM-dd HH:mm in the player's local time, shared by the VIP and Blacklist tabs.</summary>
public static class DateInput
{
    public const string Format = "yyyy-MM-dd HH:mm";

    public static string Now() => DateTime.Now.ToString(Format, CultureInfo.InvariantCulture);

    public static string Text(DateTimeOffset at) => at.ToLocalTime().ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>The typed local time; null if it can't be read, or (unless <paramref name="future"/>) is in the future.</summary>
    public static DateTimeOffset? Parse(string text, bool future = false)
    {
        if (!DateTime.TryParseExact(text.Trim(), [Format, "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            return null;
        var at = new DateTimeOffset(local);
        return !future && at > DateTimeOffset.Now.AddMinutes(5) ? null : at;
    }

    /// <summary>The text box, red when <paramref name="parsed"/> is null, with a Now button after it.</summary>
    public static void Draw(string id, ref string text, DateTimeOffset? parsed, float width = -1)
    {
        var now = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var field = width < 0 ? ImGui.GetContentRegionAvail().X - now - spacing : width - now - spacing;
        ImGui.SetNextItemWidth(field);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Red, parsed == null))
            ImGui.InputTextWithHint(id, Format, ref text, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Your local time, as {Format}");
        ImGui.SameLine();
        if (ImGuiComponents.IconButton($"{id}now", FontAwesomeIcon.Clock))
            text = Now();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Now");
    }
}
