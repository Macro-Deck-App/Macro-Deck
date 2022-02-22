using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Backups;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;
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
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck
{
    public static class MacroDeck
    {
        static Assembly assembly = Assembly.GetExecutingAssembly();
        internal static readonly string VersionString = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
        internal static readonly int BuildVersion = Int32.Parse(VersionString.Split(".")[3].ToString());

        public static readonly int ApiVersion = 20;
        public static readonly int PluginApiVersion = 31;

        // Start parameters
        internal static bool ForceUpdate = false;
        internal static bool TestUpdateChannel = false;
        internal static bool ExportDefaultStrings = false;
        internal static bool SafeMode = false;
        internal static bool PortableMode = false;
        // Start parameters end

        public static readonly string ExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
        internal static readonly string MainDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
        public static string UserDirectoryPath;
        public static string PluginsDirectoryPath;
        public static string UpdatePluginsDirectoryPath;
        public static string TempDirectoryPath;
        public static string IconPackDirectoryPath;
        public static string PluginCredentialsPath;
        public static string PluginConfigPath;
        public static string BackupsDirectoryPath;
        public static string LogsDirectoryPath;

        public static string ConfigFilePath;
        public static string DevicesFilePath;
        public static string VariablesFilePath;
        public static string ProfilesFilePath;

        private static void InitializePaths(bool portable)
        {
            if (portable)
            {
                UserDirectoryPath = Path.Combine(MainDirectoryPath, "Data");
            } else
            {
                UserDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macro Deck");
            }
            PluginsDirectoryPath = Path.Combine(UserDirectoryPath, "plugins");
            UpdatePluginsDirectoryPath = Path.Combine(PluginsDirectoryPath, ".updates");
            TempDirectoryPath = Path.Combine(UserDirectoryPath, ".temp");
            IconPackDirectoryPath = Path.Combine(UserDirectoryPath, "iconpacks");
            PluginCredentialsPath = Path.Combine(UserDirectoryPath, "credentials");
            PluginConfigPath = Path.Combine(UserDirectoryPath, "configs");
            BackupsDirectoryPath = Path.Combine(UserDirectoryPath, "backups");
            LogsDirectoryPath = Path.Combine(UserDirectoryPath, "logs");

            ConfigFilePath = Path.Combine(UserDirectoryPath, "config.json");
            DevicesFilePath = Path.Combine(UserDirectoryPath, "devices.json");
            VariablesFilePath = Path.Combine(UserDirectoryPath, "variables.db");
            ProfilesFilePath = Path.Combine(UserDirectoryPath, "profiles.db");
        }

        public static event EventHandler OnMainWindowLoad;

        private static Configuration.Configuration _configuration = new Configuration.Configuration();

        public static Configuration.Configuration Configuration { get { return _configuration; } }

        private static ContextMenuStrip _trayIconContextMenu = new ContextMenuStrip();

        private static NotifyIcon _trayIcon = new NotifyIcon
        {
            Icon = Properties.Resources.appicon,
            Text = "Macro Deck " + VersionString,
            Visible = true,
            ContextMenuStrip = _trayIconContextMenu
        };

        private static long _macroDeckStarted = DateTimeOffset.Now.ToUnixTimeMilliseconds();

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

            //AppDomain.CurrentDomain.ProcessExit += OnApplicationExit;
            // Check for start arguments

            int port = -1;
            bool show = false;
            StartParameters = args;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--port":
                        if (args[i + 1] == null) break;
                        port = Int32.Parse(args[i + 1]);
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
                        if (args[i + 1] != null && Enum.TryParse(typeof(LogLevel), args[i + 1], true, out object logLevel))
                        {
                            MacroDeckLogger.LogLevel = (LogLevel)logLevel;
                        }
                        break;
                    case "--debug-console":
                        MacroDeckLogger.StartDebugConsole();
                        break;
                }
            }


            InitializePaths(PortableMode);

            MacroDeckLogger.Info(Environment.NewLine + "==========================================");
            MacroDeckLogger.Info("Starting Macro Deck version " + VersionString + (args.Length > 0 ? " with parameters: " + string.Join(" ", args) : ""));
            MacroDeckLogger.Info("Executable: " + ExecutablePath);
            MacroDeckLogger.Info("OS: " + OperatingSystemInformation.GetWindowsVersionName());
            string networkInterfaces = "";
            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in adapters)
                {
                    networkInterfaces += Environment.NewLine + $"{adapter.Name} - {adapter.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault().Address.ToString()}";
                }
            }
            catch { }
            MacroDeckLogger.Info($"Network interfaces: {networkInterfaces}");

            // Check if Macro Deck is already running
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location)).Count() > 1)
            {
                MacroDeckLogger.Warning("Macro Deck is already running");
                using (var messageBox = new GUI.CustomControls.MessageBox())
                {
                    messageBox.ShowDialog("Macro Deck is already running", "You can't start more than one instance of Macro Deck.", MessageBoxButtons.OK);
                }
                Environment.Exit(0);
                return;
            }


            // Check if directories exist
            if (!Directory.Exists(UserDirectoryPath))
            {
                try
                {
                    Directory.CreateDirectory(UserDirectoryPath);
                    MacroDeckLogger.Info("Successfully created " + UserDirectoryPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create user directory: " + ex.Message);
                }
            }

            if (!Directory.Exists(LogsDirectoryPath))
            {
                try
                {
                    Directory.CreateDirectory(LogsDirectoryPath);
                    MacroDeckLogger.Info("Successfully created " + LogsDirectoryPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create logs directory: " + ex.Message);
                }
            }


            if (!Directory.Exists(PluginCredentialsPath))
            {
                try
                {
                    Directory.CreateDirectory(PluginCredentialsPath);
                    MacroDeckLogger.Info("Successfully created " + PluginCredentialsPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create plugin credentials directory: " + ex.Message);
                }
            }

            if (!Directory.Exists(PluginConfigPath))
            {
                try
                {
                    Directory.CreateDirectory(PluginConfigPath);
                    MacroDeckLogger.Info("Successfully created " + PluginConfigPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create plugin config directory: " + ex.Message);
                }
            }

            if (!Directory.Exists(BackupsDirectoryPath))
            {
                try
                {
                    Directory.CreateDirectory(BackupsDirectoryPath);
                    MacroDeckLogger.Info("Successfully created " + BackupsDirectoryPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create backups directory: " + ex.Message);
                }
            }

            BackupManager.CheckRestoreDirectory();


            if (!Directory.Exists(TempDirectoryPath))
            {
                try
                {
                    Directory.CreateDirectory(TempDirectoryPath);
                    MacroDeckLogger.Info("Successfully created " + TempDirectoryPath);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Failed to create temp directory: " + ex.Message);
                }
            } else
            {
                // Clean up temp directory
                DirectoryInfo di = new DirectoryInfo(TempDirectoryPath);

                foreach (FileInfo file in di.GetFiles())
                {
                    try
                    {
                        file.Delete();
                    } catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Failed to clean up files in the temp directory: " + ex.Message);
                    }
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    try
                    {
                        dir.Delete(true);
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning("Failed to clean up folders in the temp directory: " + ex.Message);
                    }
                }
            }

            Language.LanguageManager.Load(ExportDefaultStrings);

            if (IsAdministrator())
            {
                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    msgBox.ShowDialog("Macro Deck started with administrator privileges", "It's not recommended to start Macro Deck with administrator privileges.", MessageBoxButtons.OK);
                }
            }

            // Check if config exists
            if (!File.Exists(ConfigFilePath))
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
                _configuration = JsonConvert.DeserializeObject<Configuration.Configuration>(File.ReadAllText(ConfigFilePath));
                MacroDeckLogger.Info("Successfully read configuration file");
                Start(show, port);
            }
            
        }

        public static void SaveConfiguration()
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.NullValueHandling = NullValueHandling.Ignore;

            try
            {
                using StreamWriter sw = new StreamWriter(ConfigFilePath);
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
            Language.LanguageManager.SetLanguage(_configuration.Language);
            CreateTrayIcon();
            _ = new HotkeyManager();
            VariableManager.Load();
            PluginManager.Load();
            PluginManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;
            PluginManager.EnablePlugins();
            IconManager.LoadIconPacks();
            IconManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;
            ProfileManager.Load();

            MacroDeckServer.Start(_configuration.Host_Address, port == -1 ? _configuration.Host_Port : port);
            BroadcastServer.Start();

            Updater.Updater.Initialize(ForceUpdate, TestUpdateChannel);
            Updater.Updater.OnUpdateAvailable += OnUpdateAvailable;

            ProfileManager.AddVariableChangedListener();
            ProfileManager.AddWindowFocusChangedListener();


            long startTook = DateTimeOffset.Now.ToUnixTimeMilliseconds() - _macroDeckStarted;
            MacroDeckLogger.Info($"Macro Deck startup finished (took {startTook}ms)");

            if (show || SafeMode)
            {
                ShowMainWindow();
            }

            Application.Run();
        }

        private static void OnUpdateAvailable(object sender, EventArgs e)
        {
            Updater.Updater.OnUpdateAvailable -= OnUpdateAvailable;
            JObject versionObject = sender as JObject;
            try
            {
                _trayIcon.ShowBalloonTip(5000, "Macro Deck Updater", String.Format(Language.LanguageManager.Strings.VersionXIsNowAvailable, versionObject["version"].ToString(), versionObject["channel"].ToString()), ToolTipIcon.Info);
            } catch { }
        }

        private static void CreateTrayIcon()
        {
            ToolStripMenuItem showItem = new ToolStripMenuItem
            {
                Text = Language.LanguageManager.Strings.Show,
                Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            showItem.Click += ShowItemClick;

            ToolStripMenuItem restartItem = new ToolStripMenuItem
            {
                Text = Language.LanguageManager.Strings.Restart,
                Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            restartItem.Click += RestartItemClick;

            ToolStripMenuItem exitItem = new ToolStripMenuItem
            {
                Text = Language.LanguageManager.Strings.Exit,
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
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(ExecutablePath)
                {
                    UseShellExecute = true,
                    Arguments = (mainWindow != null && !mainWindow.IsDisposed ? "--show " : "") + parameters
                }
            };
            p.Start();
            Environment.Exit(0);
        }

        private static void ExitItemClick(object sender, EventArgs e)
        {
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
            if (Application.OpenForms.OfType<MainWindow>().Count() > 0)
            {
                mainWindow.WindowState = FormWindowState.Minimized;
                mainWindow.Show();
                mainWindow.WindowState = FormWindowState.Normal;
                return;
            }
            mainWindow = new MainWindow();
            mainWindow.Load += MainWindowLoadEvent;
            mainWindow.FormClosed += MainWindow_FormClosed;
            mainWindow.Show();
            MacroDeckLogger.Trace("MainWindow created");
        }

        private static void MainWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            mainWindow.Load -= MainWindowLoadEvent;
            mainWindow.FormClosed -= MainWindow_FormClosed;
            mainWindow.Dispose();
            MacroDeckLogger.Trace("MainWindow disposed");
        }

        private static void MainWindowLoadEvent(object sender, EventArgs e)
        {
            if (OnMainWindowLoad != null)
            {
                OnMainWindowLoad((MainWindow)sender, EventArgs.Empty);
            }
        }


        private static void OnApplicationExit(object sender, EventArgs e)
        {
            VariableManager.Save();
            MacroDeckLogger.Info("Exiting Macro Deck...");
        }

        private static void OnPackageManagerUpdateCheckFinished(object sender, EventArgs e)
        {
            if (PluginManager.PluginsUpdateAvailable.Count > 0)
            {
                PluginManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
                IconManager.OnUpdateCheckFinished -= OnPackageManagerUpdateCheckFinished;
                _trayIcon.ShowBalloonTip(5000, "Macro Deck Package Manager", Language.LanguageManager.Strings.UpdatesAvailable, ToolTipIcon.Info);
            }
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MacroDeckLogger.Error(typeof(MacroDeck), "CurrentDomainOnUnhandledException: " + e.ExceptionObject.ToString());
            //ShowCrashReport(e.ExceptionObject.ToString());
        }

        private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MacroDeckLogger.Error(typeof(MacroDeck), "ApplicationThreadException: " + e.Exception.Message + Environment.NewLine + e.Exception.StackTrace);
            //ShowCrashReport(e.Exception.Message + Environment.NewLine + Environment.NewLine + e.Exception.StackTrace);
        }

        /*private static void ShowCrashReport(string crashReport)
        {
            foreach (GUI.CustomControls.Form form in Application.OpenForms)
            {
                if (form.Name.Equals("CrashReportDialog"))
                {
                    return;
                }
            }

            using (var crashReportDialog = new CrashReportDialog(crashReport))
            {
                crashReportDialog.ShowDialog();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }*/

        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
