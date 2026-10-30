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

        if (plugin.Auth.CurrentAccess.Court)
            changed |= DrawCourtBell(config);

        if (changed)
            plugin.SaveConfig();
    }

    /// <summary>The bell for court sessions running out. Returns true if anything changed.</summary>
    private bool DrawCourtBell(Configuration config)
    {
        var changed = false;
        ImGuiHelpers.ScaledDummy(4);
        Theme.SectionLabel("Court");

        var warning = config.CourtBellWarning;
        if (ImGui.Checkbox("##bellWarning", ref warning))
        {
            config.CourtBellWarning = warning;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.Text("Ring");
        ImGui.SameLine();
        var minutes = config.CourtBellMinutes;
        ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!warning))
        {
            if (ImGui.InputInt("##bellMinutes", ref minutes, 1, 5))
            {
                config.CourtBellMinutes = Math.Clamp(minutes, 1, 60);
                changed = true;
            }
        }
        ImGui.SameLine();
        ImGui.Text("minutes before a session's time runs out");

        var atEnd = config.CourtBellAtEnd;
        if (ImGui.Checkbox("Ring again when the time runs out", ref atEnd))
        {
            config.CourtBellAtEnd = atEnd;
            changed = true;
        }

        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("Sound", config.CourtBellSound == 0 ? "No sound" : $"<se.{config.CourtBellSound}>"))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("No sound (chat line and toast only)", config.CourtBellSound == 0))
                {
                    config.CourtBellSound = 0;
                    changed = true;
                }
                for (var i = 1; i <= 16; i++)
                {
                    if (ImGui.Selectable($"<se.{i}>", config.CourtBellSound == i))
                    {
                        config.CourtBellSound = i;
                        plugin.CourtBell.Ring(); // Let them hear what they picked.
                        changed = true;
                    }
                }
            }
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(config.CourtBellSound == 0))
        {
            if (ImGui.Button("Test"))
                plugin.CourtBell.Ring();
        }
        ImGui.TextDisabled("Only you hear it. A chat line and a toast show too, in case your game sound is off.");
        return changed;
    }
}
