using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Backups;
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
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck
{
    public class MacroDeck : NativeWindow
    {
        static readonly Assembly assembly = Assembly.GetExecutingAssembly();
        public static readonly VersionModel Version = new(FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);

        public static readonly int ApiVersion = 20;
        public static readonly int PluginApiVersion = 40;

        public static ApplicationPaths? ApplicationPaths;

        // Start parameters
        internal static bool ForceUpdate;
        internal static bool TestUpdateChannel;
        internal static bool ExportDefaultStrings;
        internal static bool SafeMode = false;
        internal static bool PortableMode;
        // Start parameters end
        
        private static Stopwatch _startUpTimeStopWatch = new();

        internal static SynchronizationContext SyncContext { get; set; }
        
        public static event EventHandler OnMainWindowLoad;

        public static event EventHandler OnMacroDeckLoaded;

        private static Configuration.Configuration _configuration = new();

        public static Configuration.Configuration Configuration => _configuration;

        private static ContextMenuStrip _trayIconContextMenu = new();

        private static NotifyIcon _trayIcon = new()
        {
            Icon = Resources.appicon,
            Text = "Macro Deck " + Version.VersionString,
            Visible = false,
            ContextMenuStrip = _trayIconContextMenu
        };

        private static MainWindow mainWindow;

        public static MainWindow MainWindow
        {
            get
            {
                if (mainWindow != null && !mainWindow.IsDisposed && mainWindow.Visible)
                {
                    return mainWindow;
                }
                return null;
            }
        }

        public static string[] StartParameters;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Register exception event handlers
            
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += ApplicationThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

            _startUpTimeStopWatch = new Stopwatch();
            _startUpTimeStopWatch.Start();

            // Check if Macro Deck is already running
            var proc = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(proc.ProcessName);
            if (processes.Length > 1)
            {
                MacroDeckLogger.Warning("Detected running instance, trying to show it...");
                if (MacroDeckPipeClient.SendShowMainWindowMessage())
                {
                    MacroDeckLogger.Warning("Running instance should now be visible. Closing this instance...");
                    Environment.Exit(0);
                    return;
                }

                MacroDeckLogger.Warning("Running instance seems not to respond, killing it and continue this instance...");
                foreach (var p in processes.Where(x => x.Id != proc.Id))
                {
                    try
                    {
                        p.Kill();
                        MacroDeckLogger.Info($"Killed pid {p.Id}");
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning($"Could not kill pid {p.Id}: {ex.Message}");
                    }
                }
            }

            // Check for start arguments
            var port = -1;
            var show = false;
            StartParameters = args;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--port":
                        if (args[i + 1] == null) break;
                        port = int.Parse(args[i + 1]);
                        break;
                    case "--show":
                        show = true;
                        break;
                    case "--force-update":
                        ForceUpdate = true;
                        break;
                    case "--test-channel":
                        TestUpdateChannel = true;
                        break;
                    case "--export-default-strings":
                        ExportDefaultStrings = true;
                        break;
                    case "--portable":
                        PortableMode = true;
                        break;
                    case "--disable-file-logging":
                        MacroDeckLogger.FileLogging = false;
                        break;
                    case "--loglevel":
                        if (args[i + 1] != null && Enum.TryParse(typeof(LogLevel), args[i + 1], true, out var logLevel))
                        {
                            MacroDeckLogger.LogLevel = (LogLevel)logLevel;
                        }
                        break;
                    case "--debug-console":
                        MacroDeckLogger.StartDebugConsole();
                        break;
                }
            }


            ApplicationPaths = new ApplicationPaths(PortableMode);

            MacroDeckLogger.CleanUpLogsDir();

            MacroDeckLogger.Info(Environment.NewLine + "==========================================");
            MacroDeckLogger.Info("Starting Macro Deck version " + Version.VersionString + " build " + Version.Build + (args.Length > 0 ? " with parameters: " + string.Join(" ", args) : ""));
            MacroDeckLogger.Info("Executable: " + ApplicationPaths.ExecutablePath);
            MacroDeckLogger.Info("OS: " + OperatingSystemInformation.GetWindowsVersionName());
            var networkInterfaces = "";
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                networkInterfaces = adapters.Aggregate(networkInterfaces, (current, adapter) => current + (Environment.NewLine + $"{adapter.Name} - {adapter.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault().Address}"));
            }
            catch
            {
                // ignored
            }

            MacroDeckLogger.Info($"Network interfaces: {networkInterfaces}");

            
            BackupManager.CheckRestoreDirectory();

            LanguageManager.Load(ExportDefaultStrings);

            if (IsAdministrator())
            {
                MacroDeckLogger.Info("Macro Deck started with administrator privileges");
            }

            // Initializing Cef Browser
            try
            {
                var settings = new CefSettings();
                settings.CefCommandLineArgs.Add("force-device-scale-factor", "1");
                settings.CefCommandLineArgs.Add("disable-gpu-vsync", "1");
                settings.CefCommandLineArgs.Add("disable-gpu", "1");
                settings.CachePath = Path.Combine(ApplicationPaths.UserDirectoryPath, "CefSharp", "Cache");

                Cef.Initialize(settings);
            } catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(MacroDeck), $"Could not initialize Chromium Embedded Framework: {ex.Message}" + Environment.NewLine + "Make sure, Microsoft Visual C++ Redistributable is installed on your computer. You can download it here: https://aka.ms/vs/17/release/vc_redist.x64.exe");
            }

            // Check if config exists
            if (!File.Exists(ApplicationPaths.MainConfigFilePath))
            {
                MacroDeckLogger.Info("Config file not found. Entering initial setup wizard...");
                // Start initial setup
                using (var initialSetup = new InitialSetup())
                {
                    Application.Run(initialSetup);
                    if (initialSetup.DialogResult == DialogResult.OK)
                    {
                        _configuration = initialSetup.configuration;
                        SaveConfiguration();
                        MacroDeckLogger.Info("Configuration saved. Restarting Macro Deck...");
                        using (var defenderFirewallAlertInfo = new DefenderFirewallAlert())
                        {
                            defenderFirewallAlertInfo.ShowDialog();
                        }
                        RestartMacroDeck("--show");
                    } else
                    {
                        Environment.Exit(0);
                    }
                }
            }
            else
            {
                // Read config
                _configuration = JsonConvert.DeserializeObject<Configuration.Configuration>(File.ReadAllText(ApplicationPaths.MainConfigFilePath)) ?? new Configuration.Configuration();
                MacroDeckLogger.Info("Successfully read configuration file");
                Start(show, port);
            }
            
        }

        public static void SaveConfiguration()
        {
            var serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore
            };

            try
            {
                using var sw = new StreamWriter(ApplicationPaths.MainConfigFilePath);
                using JsonWriter writer = new JsonTextWriter(sw);
                serializer.Serialize(writer, _configuration);

                MacroDeckLogger.Info("Configuration saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Failed to save configuration: " + ex.Message);
            }
        }

        private static void Start(bool show = false, int port = -1)
        {
            LanguageManager.SetLanguage(_configuration.Language);
            Colors.Initialize();
            _ = new HotkeyManager();
            VariableManager.Initialize();
            PluginManager.Load();
            PluginManager.EnablePlugins();
            IconManager.Initialize();
            ProfileManager.Load();

            MacroDeckServer.Start(_configuration.Host_Address, port == -1 ? _configuration.Host_Port : port);
            BroadcastServer.Start();
            ADBServerHelper.Initialize();

            ProfileManager.AddVariableChangedListener();
            ProfileManager.AddWindowFocusChangedListener();

            MacroDeckPipeServer.Initialize();
            MacroDeckPipeServer.PipeMessage += MacroDeckPipeServer_PipeMessage;

            CreateTrayIcon();

            using (mainWindow = new MainWindow())
            {
                SyncContext = SynchronizationContext.Current;
            }

            _startUpTimeStopWatch.Stop();

            var startTook = _startUpTimeStopWatch.Elapsed.TotalMilliseconds;
            MacroDeckLogger.Info($"Macro Deck startup finished (took {startTook}ms)");

            OnMacroDeckLoaded?.Invoke(null, EventArgs.Empty);

            Updater.Updater.OnUpdateAvailable += OnUpdateAvailable;
            Updater.Updater.Initialize(ForceUpdate, TestUpdateChannel);
            ExtensionStoreHelper.SearchUpdatesAsync();

            if (show || SafeMode)
            {
                ShowMainWindow();
            }

            Application.Run();
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

        private static void OnUpdateAvailable(object sender, EventArgs e)
        {
            Updater.Updater.OnUpdateAvailable -= OnUpdateAvailable;
            var versionObject = sender as JObject;
            try
            {
                var btnOpenSettings = new ButtonPrimary
                {
                    AutoSize = true,
                    Text = LanguageManager.Strings.OpenSettings,
                };
                btnOpenSettings.Click += (sender, e) =>
                {
                    MainWindow?.SetView(new SettingsView(2));
                };
                NotificationManager.SystemNotification("Macro Deck Updater", string.Format(LanguageManager.Strings.VersionXIsNowAvailable, versionObject["version"], versionObject["channel"]), true, new List<Control> { btnOpenSettings }, Resources.Macro_Deck_2021_update);
            } catch { }
        }

        internal static void ShowBalloonTip(string title, string message)
        {
            try
            {
                _trayIcon?.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
            }
            catch { }
        }

        private static void CreateTrayIcon()
        {
            _trayIcon.Visible = true;

            var showItem = new ToolStripMenuItem
            {
                Text = LanguageManager.Strings.Show,
                Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            showItem.Click += ShowItemClick;

            var restartItem = new ToolStripMenuItem
            {
                Text = LanguageManager.Strings.Restart,
                Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            restartItem.Click += RestartItemClick;

            var exitItem = new ToolStripMenuItem
            {
                Text = LanguageManager.Strings.Exit,
                Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            exitItem.Click += ExitItemClick;

            _trayIconContextMenu.Items.Add(showItem);
            _trayIconContextMenu.Items.Add(restartItem);
            _trayIconContextMenu.Items.Add(exitItem);

            _trayIcon.MouseDown += OnTrayIconMouseDown;
        }

        private static void ShowItemClick(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private static void RestartItemClick(object sender, EventArgs e)
        {
            RestartMacroDeck();
        }

        public static void RestartMacroDeck(string parameters = "")
        {
            _trayIcon.Visible = false;
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(ApplicationPaths.ExecutablePath)
                {
                    UseShellExecute = true,
                    Arguments = (!mainWindow.IsDisposed ? "--show " : "") + string.Join(" ", StartParameters),
                }
            };
            p.Start();
            Environment.Exit(0);
        }

        private static void ExitItemClick(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            Environment.Exit(0);
        }

        private static void OnTrayIconMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
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
            if (Application.OpenForms.OfType<MainWindow>().Any() && mainWindow is { IsDisposed: false })
            {
                if (mainWindow.InvokeRequired)
                {
                    mainWindow.Invoke(ShowMainWindow);
                    return;
                }
                mainWindow.WindowState = FormWindowState.Minimized;
                mainWindow.Show();
                mainWindow.WindowState = FormWindowState.Normal;
                return;
            }
            mainWindow = new MainWindow();
            mainWindow.Load += MainWindowLoadEvent;
            mainWindow.FormClosed += MainWindow_FormClosed;
            mainWindow.Show();
        }

        

        private static void MainWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            mainWindow.Load -= MainWindowLoadEvent;
            mainWindow.FormClosed -= MainWindow_FormClosed;
            mainWindow.Dispose();
        }

        private static void MainWindowLoadEvent(object sender, EventArgs e)
        {
            OnMainWindowLoad?.Invoke((MainWindow)sender, EventArgs.Empty);
        }

        private static void OnPackageManagerUpdateCheckFinished(object sender, EventArgs e)
        {
            if (PluginManager.PluginsUpdateAvailable.Count <= 0) return;
            IconManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
            _trayIcon.ShowBalloonTip(5000, "Macro Deck Package Manager", LanguageManager.Strings.UpdatesAvailable, ToolTipIcon.Info);
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MacroDeckLogger.Error(typeof(MacroDeck), "CurrentDomainOnUnhandledException: " + e.ExceptionObject);
        }

        private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MacroDeckLogger.Error(typeof(MacroDeck), "ApplicationThreadException: " + e.Exception.Message + Environment.NewLine + e.Exception.StackTrace);
        }

        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

    }
}
