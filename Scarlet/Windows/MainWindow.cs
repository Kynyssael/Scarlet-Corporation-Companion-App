using System.Numerics;
using Dalamud.Bindings.ImGui;
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

    private readonly Plugin plugin;

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
    }

    public void Dispose() { }

    // The venue can change after a sync; the ### id keeps the window's position.
    public override void PreDraw() => WindowName = $"{plugin.Configuration.VenueName} - Scarlet Corporation Companion App###ScarletMain";

    public override void Draw()
    {
        var access = plugin.Auth.CurrentAccess;
        if (!access.Any)
        {
            AccountControls.DrawLocked(plugin);
            return;
        }

        using var bar = ImRaii.TabBar("##scarletTabs");
        if (!bar.Success)
            return;

        if (access.Register)
            DrawPage("Register", register.Draw);
        if (access.Rooms)
            DrawPage("Rooms", rooms.Draw);
        // Bartenders only see unsent nights, on the Register; the full history is for management.
        if (access.Manage)
        {
            DrawPage("History", history.Draw);
            DrawPage("Menu", menu.Draw);
            DrawPage("Staff", staff.Draw);
        }
    }

    private static void DrawPage(string label, Action draw)
    {
        using var item = ImRaii.TabItem(label);
        if (item.Success)
            draw();
    }
}
