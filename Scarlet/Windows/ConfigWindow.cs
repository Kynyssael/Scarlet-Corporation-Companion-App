using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Scarlet.Services;

namespace Scarlet.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Theme.Scope theme = new();

    public ConfigWindow(Plugin plugin) : base("Scarlet Corporation Companion App Settings###ScarletConfig")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(440, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void PreDraw() => theme.Push();
    public override void PostDraw() => theme.Pop();

    public override void Draw()
    {
        var config = plugin.Configuration;
        var changed = false;

        Theme.SectionLabel("Account");
        AccountControls.Draw(plugin);

        ImGuiHelpers.ScaledDummy(4);
        Theme.SectionLabel("Scarlet Corporation website");

        VenuePicker.Draw(plugin);

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

        ImGuiHelpers.ScaledDummy(4);
        Theme.SectionLabel("Register");

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
