using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Scarlet.Services;
using Scarlet.Windows;

namespace Scarlet;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/scarlet";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;

    private readonly Configuration realConfig;
    private readonly PosService realPos;
    private Configuration? demoConfig;
    private PosService? demoPos;

    /// <summary>The saved settings; in the dev demo, a copy that's never saved.</summary>
    public Configuration Configuration => demoConfig ?? realConfig;

    /// <summary>Tonight's tallies; in the dev demo, a separate set that's thrown away.</summary>
    public PosService Pos => demoPos ?? realPos;

    /// <summary>Dev plugins only: the offline demo (see <see cref="SetDemo"/>) is on.</summary>
    public bool Demo => demoConfig != null;

    public AuthService Auth { get; }
    public GameItems GameItems { get; } = new();
    public CourtBell CourtBell { get; }

    // Everything that talks to the website. Made again when the demo is switched on or off, so nothing
    // loaded from one side shows on the other.
    public WebSyncService WebSync { get; private set; } = null!;
    public ReportUploader Reports { get; private set; } = null!;
    public VenueNights VenueNights { get; private set; } = null!;
    public RoomLocks RoomLocks { get; private set; } = null!;
    public VipList Vips { get; private set; } = null!;
    public BanList Bans { get; private set; } = null!;
    public ShiftList Shifts { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("Scarlet");
    private MainWindow MainWindow { get; }
    private ConfigWindow ConfigWindow { get; }

    public Plugin()
    {
        realConfig = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (realConfig.MigrateStaffVenues())
            SaveConfig();

        realPos = new PosService(this, new PosStore(PluginInterface.GetPluginConfigDirectory()));
        Auth = new AuthService(this, PluginInterface.GetPluginConfigDirectory());
        CourtBell = new CourtBell(this);
        StartWebsiteServices();
        if (Configuration.SyncOnStartup)
            WebSync.Sync();

        Theme.LoadFonts(PluginInterface.UiBuilder.FontAtlas);
        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Scarlet Corporation Companion App. Subcommands: config, sync",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        ConfigWindow.Dispose();
        Theme.Dispose();

        StopWebsiteServices();
        CourtBell.Dispose();
        Auth.Dispose();
        DemoSite.On = false;
        DemoSite.Handler.Dispose();
        realPos.Save();
    }

    /// <summary>Saves the settings; does nothing in the demo, whose settings are thrown away.</summary>
    public void SaveConfig()
    {
        if (!Demo)
            PluginInterface.SavePluginConfig(realConfig);
    }

    private void StartWebsiteServices()
    {
        // Before Reports: the uploader starts sending at once and refreshes VenueNights when a night goes through.
        VenueNights = new VenueNights(this);
        Reports = new ReportUploader(this);
        RoomLocks = new RoomLocks(this);
        Vips = new VipList(this);
        Bans = new BanList(this);
        Shifts = new ShiftList(this);
        WebSync = new WebSyncService(this);
    }

    private void StopWebsiteServices()
    {
        WebSync.Dispose();
        Reports.Dispose();
        VenueNights.Dispose();
        RoomLocks.Dispose();
        Vips.Dispose();
        Bans.Dispose();
        Shifts.Dispose();
    }

    /// <summary>
    /// Dev plugins only: the offline demo. Every tab works as a manager of every venue, on sample data
    /// answered inside the plugin (<see cref="DemoSite"/>), without staff roles or signing in. Nothing reaches
    /// the website, and the demo's settings, tallies and changes are thrown away when it's left; the real
    /// ones are untouched. Call on the main thread.
    /// </summary>
    public void SetDemo(bool on)
    {
        if (on == Demo || (on && !PluginInterface.IsDev))
            return;

        StopWebsiteServices();
        if (on)
        {
            DemoSite.Reset();
            var demoDirectory = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "demo");
            try
            {
                File.Delete(Path.Combine(demoDirectory, "pos-data.json")); // Start each demo fresh.
            }
            catch (IOException)
            {
                // Then last demo's tallies show; harmless.
            }
            demoConfig = new Configuration
            {
                WebsiteUrl = realConfig.WebsiteUrl,
                VenueSlug = realConfig.VenueSlug,
                VenueName = realConfig.VenueName,
                CurrencySuffix = realConfig.CurrencySuffix,
                EchoNightTotal = realConfig.EchoNightTotal,
                CourtBellWarning = realConfig.CourtBellWarning,
                CourtBellMinutes = realConfig.CourtBellMinutes,
                CourtBellAtEnd = realConfig.CourtBellAtEnd,
                CourtBellSound = realConfig.CourtBellSound,
            };
            demoPos = new PosService(this, new PosStore(demoDirectory));
        }
        else
        {
            demoConfig = null;
            demoPos = null;
            Auth.ViewAs = DevRole.Real;
        }
        DemoSite.On = on;
        Auth.Demo = on;
        StartWebsiteServices();
        WebSync.Sync();
    }

    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    /// <summary>Name of the logged-in character.</summary>
    public static string? LocalPlayerName() => ObjectTable.LocalPlayer?.Name.TextValue;

    public string FormatGil(long amount) => $"{amount:N0} {Configuration.CurrencySuffix}";

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.FirstOrDefault()?.ToLowerInvariant())
        {
            case "config":
                ToggleConfigUi();
                break;
            case "sync":
                WebSync.Sync();
                break;
            default:
                ToggleMainUi();
                break;
        }
    }
}
