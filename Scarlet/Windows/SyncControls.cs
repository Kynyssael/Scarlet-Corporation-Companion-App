using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace Scarlet.Windows;

/// <summary>"Sync from website" button with last-sync status, shared by the settings and menu pages.</summary>
public static class SyncControls
{
    public static void Draw(Plugin plugin)
    {
        var sync = plugin.WebSync;
        using (ImRaii.Disabled(sync.Syncing))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CloudDownloadAlt, sync.Syncing ? "Syncing..." : "Sync from website"))
                sync.Sync();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        if (sync.Status.Length > 0)
            ImGui.TextDisabled(sync.Status);
        else if (plugin.Configuration.LastSyncedAt is { } last)
            ImGui.TextDisabled($"Last synced {last:g}");
        else
            ImGui.TextDisabled("Never synced");
    }
}
