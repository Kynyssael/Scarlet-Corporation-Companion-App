using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows;

/// <summary>Venue dropdown, shared by the main window and settings. Switching loads that venue's menu and staff.</summary>
public static class VenuePicker
{
    public static void Draw(Plugin plugin, string label = "Venue")
    {
        var config = plugin.Configuration;
        var state = plugin.Auth.State;

        using var combo = ImRaii.Combo(label, ScarletWebClient.VenueName(config.VenueSlug));
        if (!combo.Success)
            return;

        // Venues the person can use first.
        foreach (var (slug, name) in ScarletWebClient.Venues.OrderBy(v => state.AccessFor(v.Slug).Any ? 0 : 1))
        {
            var usable = state.AccessFor(slug).Any;
            using var dim = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], !usable);
            if (ImGui.Selectable($"{(usable ? name : $"{name} (no access)")}##{slug}", slug == config.VenueSlug) && slug != config.VenueSlug)
            {
                config.VenueSlug = slug;
                plugin.SaveConfig();
                plugin.WebSync.Sync();
            }
        }
    }
}
