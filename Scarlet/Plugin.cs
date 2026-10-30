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

    public Configuration Configuration { get; }
    public PosService Pos { get; }
    public WebSyncService WebSync { get; }
    public AuthService Auth { get; }
    public ReportUploader Reports { get; }
    public GameItems GameItems { get; } = new();
    public VenueNights VenueNights { get; }
    public RoomLocks RoomLocks { get; }
    public VipList Vips { get; }
    public BanList Bans { get; }
    public ShiftList Shifts { get; }

    public readonly WindowSystem WindowSystem = new("Scarlet");
    private MainWindow MainWindow { get; }
    private ConfigWindow ConfigWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.MigrateStaffVenues())
            SaveConfig();

        Pos = new PosService(this, new PosStore(PluginInterface.GetPluginConfigDirectory()));
        Auth = new AuthService(this, PluginInterface.GetPluginConfigDirectory());
        // Before Reports: the uploader starts sending at once and refreshes VenueNights when a night goes through.
        VenueNights = new VenueNights(this);
        Reports = new ReportUploader(this);
        RoomLocks = new RoomLocks(this);
        Vips = new VipList(this);
        Bans = new BanList(this);
        Shifts = new ShiftList(this);
        WebSync = new WebSyncService(this);
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

        WebSync.Dispose();
        Reports.Dispose();
        VenueNights.Dispose();
        RoomLocks.Dispose();
        Vips.Dispose();
        Bans.Dispose();
        Shifts.Dispose();
        Auth.Dispose();
        Pos.Save();
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Configuration);

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
