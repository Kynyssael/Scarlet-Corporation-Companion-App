using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Scarlet.Services;
using Scarlet.Windows.Pages;

namespace Scarlet.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly RegisterPage register;
    private readonly MenuPage menu;
    private readonly HistoryPage history;
    private readonly StaffPage staff;
    private readonly RoomsPage rooms;
    private readonly PhotosPage photos;
    private readonly CourtPage court;
    private readonly VipPage vips;
    private readonly BlacklistPage blacklist;
    private readonly AttendancePage attendance;

    private readonly Plugin plugin;
    private readonly Theme.Scope theme = new();

    public MainWindow(Plugin plugin) : base($"{plugin.Configuration.VenueName} - Scarlet Corporation Companion App###ScarletMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        register = new RegisterPage(plugin);
        menu = new MenuPage(plugin);
        history = new HistoryPage(plugin);
        staff = new StaffPage(plugin);
        rooms = new RoomsPage(plugin);
        photos = new PhotosPage(plugin);
        court = new CourtPage(plugin);
        vips = new VipPage(plugin);
        blacklist = new BlacklistPage(plugin);
        attendance = new AttendancePage(plugin);
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // The venue can change after a sync; the ### id keeps the window's position.
        WindowName = $"{plugin.Configuration.VenueName} - Scarlet Corporation Companion App###ScarletMain";
        theme.Push();
    }

    public override void PostDraw() => theme.Pop();

    public override void Draw()
    {
        if (plugin.Auth.State.SignedIn)
            DrawTopBar();

        var access = plugin.Auth.CurrentAccess;
        if (!access.Any)
        {
            AccountControls.DrawLocked(plugin);
            return;
        }

        using var bar = ImRaii.TabBar("##scarletTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (!bar.Success)
            return;

        if (access.Register)
            DrawPage("Register", register.Draw);
        // Photographers: photos sold tonight. Courts: session timers. Both lock rooms.
        if (access.Photos)
            DrawPage("Photos", photos.Draw);
        if (access.Court)
            DrawPage("Court", court.Draw);
        if (access.Rooms)
            DrawPage("Rooms", rooms.Draw);
        // Everyone needs to know who's a VIP and who not to serve; management edits both.
        // Management sees how many VIPs need renewing without opening the tab.
        var due = access.Manage ? vips.NeedRenewing : 0;
        DrawPage(due > 0 ? $"VIP ({due})###vip" : "VIP###vip", vips.Draw);
        DrawPage("Blacklist", blacklist.Draw);
        // Bartenders only see unsent nights, on the Register; the full history is for management.
        if (access.Manage)
        {
            DrawPage("History", history.Draw);
            DrawPage("Attendance", attendance.Draw);
            DrawPage("Menu", menu.Draw);
            DrawPage("Staff", staff.Draw);
        }
    }

    /// <summary>Venue switcher, who's signed in and a shortcut to settings, above the tabs.</summary>
    private void DrawTopBar()
    {
        Theme.Icon(FontAwesomeIcon.Building, Theme.Rose);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        VenuePicker.Draw(plugin, "##venue");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Venue you're working at tonight");

        if (Plugin.PluginInterface.IsDev)
        {
            ImGui.SameLine();
            DrawViewAs();
        }

        if (plugin.WebSync.Syncing)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("loading menu...");
        }

        // Right side: name, the role this view shows, settings.
        var state = plugin.Auth.State;
        var name = state.Name ?? string.Empty;
        var role = RoleLabel();
        var pillPad = 14 * ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = ImGui.CalcTextSize(name).X + spacing + ImGui.CalcTextSize(role).X + pillPad + spacing + ImGui.GetFrameHeight();
        ImGui.SameLine(ImGui.GetContentRegionMax().X - width);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(name);
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.Y);
        Theme.Pill(role, plugin.Auth.ViewAs != DevRole.Real ? Theme.Amber : Theme.Rose);
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().FramePadding.Y);
        if (ImGuiComponents.IconButton("settings", FontAwesomeIcon.Cog))
            plugin.ToggleConfigUi();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");

        ImGuiHelpers.ScaledDummy(2);
    }

    /// <summary>The most senior role the current view has, for the badge in the top bar.</summary>
    private string RoleLabel()
    {
        var access = plugin.Auth.CurrentAccess;
        return access.Manage ? "Management"
            : string.Join(" + ", new[] { (access.Register, "Bartender"), (access.Photos, "Photographer"), (access.Court, "Court") }
                .Where(r => r.Item1).Select(r => r.Item2)) is { Length: > 0 } jobs ? jobs
            : "No role";
    }

    /// <summary>Dev plugins only: preview the tabs each role gets.</summary>
    private void DrawViewAs()
    {
        static string Label(DevRole role) => role switch
        {
            DevRole.Real => "My roles",
            DevRole.NoRole => "No role",
            _ => role.ToString(),
        };

        var auth = plugin.Auth;
        ImGui.SetNextItemWidth(170 * ImGuiHelpers.GlobalScale);
        // Amber while previewing, so it's obvious this isn't the real view.
        using (var amber = ImRaii.PushColor(ImGuiCol.Text, Theme.Amber, auth.ViewAs != DevRole.Real))
        using (var combo = ImRaii.Combo("##viewAs", $"View as: {Label(auth.ViewAs)}"))
        {
            amber.Pop();
            if (combo.Success)
            {
                foreach (var role in Enum.GetValues<DevRole>())
                {
                    if (ImGui.Selectable(Label(role), role == auth.ViewAs))
                        auth.ViewAs = role;
                }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Dev only: show the plugin as this role sees it. The website still checks your real roles.");
    }

    private static void DrawPage(string label, Action draw)
    {
        using var item = ImRaii.TabItem($"  {label}  ");
        if (!item.Success)
            return;
        ImGuiHelpers.ScaledDummy(2);
        draw();
    }
}
