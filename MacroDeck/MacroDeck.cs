using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.Configuration;
using SuchByte.MacroDeck.DataTypes.Updater;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Pipes;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Services;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Variables;
using Version = SuchByte.MacroDeck.DataTypes.Core.Version;

namespace SuchByte.MacroDeck;

public class MacroDeck : NativeWindow
{
    public static Version Version = 
        Version.Parse(FileVersionInfo.GetVersionInfo(ApplicationPaths.ExecutablePath).ProductVersion);

    public static readonly int ApiVersion = 20;
    public static readonly int PluginApiVersion = 40;

    public static StartParameters StartParameters { get; private set; } = new();
    public static MainConfiguration Configuration { get; private set; } = new();
    public static bool SafeMode { get; set; } = false;
    
    internal static SynchronizationContext? SyncContext { get; set; }
        
    public static event EventHandler? OnMainWindowLoad;
    public static event EventHandler? OnMacroDeckLoaded;

    private static readonly ContextMenuStrip TrayIconContextMenu = new();
    private static readonly NotifyIcon TrayIcon = new()
    {
        Icon = Resources.appicon,
        Text = @"Macro Deck " + Version.ToString(),
        Visible = false,
        ContextMenuStrip = TrayIconContextMenu
    };

    private static MainWindow? _mainWindow;
    public static MainWindow? MainWindow => 
        _mainWindow is { IsDisposed: false, Visible: true, IsHandleCreated: true } ? _mainWindow : null;


    private static readonly Stopwatch StartUpTimeStopWatch = new();
    
    internal static void Start(StartParameters startParameters)
    {
        StartParameters = startParameters;
        StartUpTimeStopWatch.Start();

        MacroDeckLogger.LogLevel = (LogLevel)StartParameters.LogLevel;
        if (StartParameters.DebugConsole)
        {
            MacroDeckLogger.StartDebugConsole();
        }

        MacroDeckLogger.Info($"Macro Deck {Version.ToString()}");
        MacroDeckLogger.Info($"Path: {ApplicationPaths.ExecutablePath}");
        MacroDeckLogger.Info($"Start parameters: {string.Join(" ", StartParameters.ToArray(StartParameters))}");

        MacroDeckLogger.CleanUpLogsDir();

        BackupManager.CheckRestoreDirectory();

        LanguageManager.Load(StartParameters.ExportDefaultStrings);

        // Check if config exists
        if (!File.Exists(ApplicationPaths.MainConfigFilePath))
        {
            StartInitialSetup();
            return;
        }
        
        Configuration = MainConfiguration.LoadFromFile(ApplicationPaths.MainConfigFilePath);
        LanguageManager.SetLanguage(Configuration.Language);
        _ = new HotkeyManager();
        VariableManager.Initialize();
        PluginManager.Load();
        PluginManager.EnablePlugins();
        IconManager.Initialize();
        ProfileManager.Load();

        PrintNetworkInterfaces();
        MacroDeckServer.Start(StartParameters.Port <= 0 ? Configuration.HostPort : StartParameters.Port);
        BroadcastServer.Start();
        ADBServerHelper.Initialize();

        ProfileManager.AddVariableChangedListener();
        ProfileManager.AddWindowFocusChangedListener();

        MacroDeckPipeServer.Initialize();
        MacroDeckPipeServer.PipeMessage += MacroDeckPipeServer_PipeMessage;

        TrayIcon.SetupTrayIcon(TrayIconContextMenu,
            ShowMainWindow,
            () => RestartMacroDeck(string.Join(" ", StartParameters)),
            Exit);

        using (_mainWindow = new MainWindow())
        {
            SyncContext = SynchronizationContext.Current;
        }

        StartUpTimeStopWatch.Stop();

        var startTook = StartUpTimeStopWatch.Elapsed.TotalMilliseconds;
        MacroDeckLogger.Info($"Macro Deck startup finished (took {startTook}ms)");

        OnMacroDeckLoaded?.Invoke(null, EventArgs.Empty);

        UpdateService.Instance().StartPeriodicalUpdateCheck();
        UpdateService.Instance().UpdateAvailable += OnUpdateAvailable;
        
        ExtensionStoreHelper.SearchUpdatesAsync();

        if (StartParameters.ShowMainWindow)
        {
            ShowMainWindow();
        }

        Application.Run();
    }

    private static void OnUpdateAvailable(object? sender, UpdateApiVersionInfo e)
    {
        var btnOpenSettings = new ButtonPrimary
        {
            AutoSize = true,
            Text = LanguageManager.Strings.OpenSettings,
        };
        btnOpenSettings.Click += (o, args) =>
        {
            MainWindow?.SetView(new SettingsView(2));
        };
        
        NotificationManager.SystemNotification(
            "Macro Deck Updater",
            string.Format(LanguageManager.Strings.VersionXIsNowAvailable, e.Version, e.IsBeta == true ? "Beta" : "Release"), 
            true, 
            new List<Control> { btnOpenSettings }, Resources.Macro_Deck_2021_update);

    }

    private static void PrintNetworkInterfaces()
    {
        StringBuilder sb = new();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var address = adapter.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                sb.AppendLine();
                sb.Append($"{adapter.Name} - {address}");
            }
        }
        catch
        {
            // ignored
        }
        MacroDeckLogger.Info($"Network interfaces: {sb}");
        sb.Clear();
    }

    private static void StartInitialSetup()
    {
        MacroDeckLogger.Info("Entering initial setup wizard...");
        // Start initial setup
        using var initialSetup = new InitialSetup();
        Application.Run(initialSetup);
        if (initialSetup.DialogResult == DialogResult.OK)
        {
            Configuration = initialSetup.configuration;
            Configuration.Save(ApplicationPaths.MainConfigFilePath);
            using var defenderFirewallAlertInfo = new DefenderFirewallAlert();
            defenderFirewallAlertInfo.ShowDialog();
            RestartMacroDeck("--show");
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static void MacroDeckPipeServer_PipeMessage(string message)
    {
        switch (message)
        {
            case "show":
                ShowMainWindow();
                break;
        }
    }

    internal static void ShowBalloonTip(string title, string message)
    {
        try
        {
            TrayIcon?.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        }
        catch
        {
            // ignored
        }
    }

    public static void RestartMacroDeck(string parameters = "")
    {
        TrayIcon.Visible = false;
        var arguments = (_mainWindow is { IsDisposed: false } ? "--show " : "") + parameters + $" --ignore-pid-check {Process.GetCurrentProcess().Id}";
        MacroDeckLogger.Info($"Restart Macro Deck with arguments: {arguments}");
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(ApplicationPaths.ExecutablePath)
            {
                UseShellExecute = true,
                Arguments = arguments,
            }
        };
        p.Start();
        Environment.Exit(0);
    }

    public static void ShowMainWindow()
    {
        if (SyncContext == null)
        {
            CreateMainForm();
        } else
        {
            SyncContext.Send(o =>
            {
                CreateMainForm();
            }, null);
        }
    }

    private static void CreateMainForm()
    {
        if (Application.OpenForms.OfType<MainWindow>().Any()
            && _mainWindow.IsDisposed == false
            && _mainWindow.IsHandleCreated)
        {
            if (_mainWindow.InvokeRequired)
            {
                _mainWindow.Invoke(ShowMainWindow);
                return;
            }
            _mainWindow.WindowState = FormWindowState.Minimized;
            _mainWindow.Show();
            _mainWindow.WindowState = FormWindowState.Normal;
            return;
        }
        _mainWindow = new MainWindow();
        _mainWindow.Load += MainWindowLoadEvent;
        _mainWindow.FormClosed += MainWindow_FormClosed;
        _mainWindow.Show();
    }
    
    private static void MainWindow_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (_mainWindow == null) return;
        _mainWindow.Load -= MainWindowLoadEvent;
        _mainWindow.FormClosed -= MainWindow_FormClosed;
        _mainWindow.Dispose();
    }

    private static void MainWindowLoadEvent(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            OnMainWindowLoad?.Invoke(window, EventArgs.Empty);
        }
    }

    public static void Exit()
    {
        ADBServerHelper.Kill();
        Environment.Exit(0);
    }
}