using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace Scarlet.Windows;

/// <summary>
/// Only shown when an ended night hasn't reached the website, so staff can resend it. Used by the
/// Register and Photos tabs; bartenders and photographers don't get the full History, management does.
/// </summary>
public static class UnsentNotice
{
    public static void Draw(Plugin plugin)
    {
        var refused = plugin.Pos.Nights.Where(n => n.UploadError != null).ToList();
        var waiting = plugin.Pos.Nights.Count(n => n.UploadError == null && n.UploadedAt == null);
        if (refused.Count == 0 && waiting == 0)
            return;

        if (refused.Count > 0)
        {
            Theme.ErrorText(refused.Count == 1 ? "A night report couldn't be sent." : $"{refused.Count} night reports couldn't be sent.");
            if (ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                foreach (var night in refused)
                    ImGui.Text($"{night.StartedAt:g}: {night.UploadError}");
            }
        }
        else
        {
            Theme.Icon(FontAwesomeIcon.PaperPlane, Theme.Amber);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Amber))
                ImGui.TextWrapped(waiting == 1 ? "Last night's report is waiting to send." : $"{waiting} night reports are waiting to send.");
            if (plugin.Reports.Status.Length > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip(plugin.Reports.Status);
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PaperPlane, refused.Count > 0 ? "Try again" : "Send now", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            plugin.Reports.RetryRefused();
        Theme.Rule();
    }
}
