using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Scarlet.Services;

namespace Scarlet.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Scarlet Corporation Companion App Settings###ScarletConfig")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(440, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var changed = false;

        ImGui.TextDisabled("Account");
        AccountControls.Draw(plugin);

        ImGui.Separator();
        ImGui.TextDisabled("Scarlet Corporation website");

        var state = plugin.Auth.State;
        using (var combo = ImRaii.Combo("Venue", ScarletWebClient.VenueName(config.VenueSlug)))
        {
            if (combo.Success)
            {
                foreach (var (slug, name) in ScarletWebClient.Venues)
                {
                    var label = !state.AccessFor(slug).Any ? $"{name} (no access)" : name;
                    if (ImGui.Selectable($"{label}##{slug}", slug == config.VenueSlug) && slug != config.VenueSlug)
                    {
                        config.VenueSlug = slug;
                        changed = true;
                        // Load that venue's menu and staff straight away.
                        plugin.WebSync.Sync();
                    }
                }
            }
        }

        var url = config.WebsiteUrl;
        if (ImGui.InputText("Website", ref url, 256))
        {
            config.WebsiteUrl = url.Trim();
            changed = true;
        }

        var syncOnStartup = config.SyncOnStartup;
        if (ImGui.Checkbox("Sync menu when the plugin loads", ref syncOnStartup))
        {
            config.SyncOnStartup = syncOnStartup;
            changed = true;
        }

        SyncControls.Draw(plugin);

        ImGui.Separator();
        ImGui.TextDisabled("Register");

        var suffix = config.CurrencySuffix;
        if (ImGui.InputText("Currency suffix", ref suffix, 16))
        {
            config.CurrencySuffix = suffix;
            changed = true;
        }

        var echo = config.EchoNightTotal;
        if (ImGui.Checkbox("Print the night's total in my chat log when I end the night", ref echo))
        {
            config.EchoNightTotal = echo;
            changed = true;
        }

        if (changed)
            plugin.SaveConfig();
    }
}
