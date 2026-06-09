using System.IO;
using Serilog;

namespace SuchByte.MacroDeck.StartupConfig;

public class ApplicationPaths
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ApplicationPaths));

    private static bool _portableMode;

    public static string ExecutablePath { get; private set; }
    public static string MainDirectoryPath { get; }
    public static string UserDirectoryPath { get; private set; } = null!;
    public static string PluginsDirectoryPath { get; private set; } = null!;
    public static string UpdatePluginsDirectoryPath { get; private set; } = null!;
    public static string TempDirectoryPath { get; private set; } = null!;
    public static string IconPackDirectoryPath { get; private set; } = null!;
    public static string PluginCredentialsPath { get; private set; } = null!;
    public static string PluginConfigPath { get; private set; } = null!;
    public static string BackupsDirectoryPath { get; private set; } = null!;
    public static string LogsDirectoryPath { get; private set; } = null!;
    public static string MainConfigFilePath { get; private set; } = null!;
    public static string DevicesFilePath { get; private set; } = null!;
    public static string VariablesFilePath { get; private set; } = null!;
    public static string ProfilesLegacyFilePath { get; private set; } = null!;
    public static string ProfilesDirectoryPath { get; private set; } = null!;

    static ApplicationPaths()
    {
        ExecutablePath = Environment.ProcessPath ??
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "Macro Deck 2.exe");
        MainDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
    }

    public static void Initialize(bool portableMode)
    {
        _portableMode = portableMode;
        InitializePaths();
        CheckPaths();
    }

    private static void InitializePaths()
    {
        UserDirectoryPath = _portableMode
            ? Path.Combine(MainDirectoryPath, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macro Deck");
        PluginsDirectoryPath = Path.Combine(UserDirectoryPath, "plugins");
        UpdatePluginsDirectoryPath = Path.Combine(PluginsDirectoryPath, ".updates");
        TempDirectoryPath = Path.Combine(UserDirectoryPath, ".temp");
        IconPackDirectoryPath = Path.Combine(UserDirectoryPath, "iconpacks");
        PluginCredentialsPath = Path.Combine(UserDirectoryPath, "credentials");
        PluginConfigPath = Path.Combine(UserDirectoryPath, "configs");
        BackupsDirectoryPath = Path.Combine(UserDirectoryPath, "backups");
        LogsDirectoryPath = Path.Combine(UserDirectoryPath, "logs");
        MainConfigFilePath = Path.Combine(UserDirectoryPath, "config.json");
        DevicesFilePath = Path.Combine(UserDirectoryPath, "devices.json");
        VariablesFilePath = Path.Combine(UserDirectoryPath, "variables.db");
        ProfilesLegacyFilePath = Path.Combine(UserDirectoryPath, "profiles.db");
        ProfilesDirectoryPath = Path.Combine(UserDirectoryPath, "profiles");
    }


    private static void CheckPaths()
    {
        Logger.Information("Checking paths...");

        void CheckCreatePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(path);
                Logger.Information("Created {Path}", path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to create {Path}", path);
            }
        }

        CheckCreatePath(UserDirectoryPath);
        CheckCreatePath(LogsDirectoryPath);
        CheckCreatePath(PluginCredentialsPath);
        CheckCreatePath(PluginConfigPath);
        CheckCreatePath(PluginsDirectoryPath);
        CheckCreatePath(BackupsDirectoryPath);
        CheckCreatePath(IconPackDirectoryPath);
        CheckCreatePath(TempDirectoryPath);
        CheckCreatePath(ProfilesDirectoryPath);

        Logger.Information("Checking paths done");
    }

    public static void CleanUpTempDirectory()
    {
        DirectoryInfo di = new(TempDirectoryPath);
        CleanupTempDir(di);


        void CleanupTempDir(DirectoryInfo directoryInfo)
        {
            if (!directoryInfo.Exists)
            {
                return;
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to delete temp file");
                }
            }

            foreach (var dir in directoryInfo.GetDirectories())
            {
                try
                {
                    dir.Delete(true);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to delete temp dir");
                }
            }
        }
    }
}
