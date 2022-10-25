using System;
using System.Diagnostics;
using System.IO;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Startup;

public class ApplicationPaths
{
    private readonly bool _portableMode = false;

    public string ExecutablePath { get; private set; } = null!;
    public string MainDirectoryPath { get; private set; } = null!;
    public string UserDirectoryPath { get; private set; } = null!;
    public string PluginsDirectoryPath { get; private set; } = null!;
    public string UpdatePluginsDirectoryPath { get; private set; } = null!;
    public string TempDirectoryPath { get; private set; } = null!;
    public string IconPackDirectoryPath { get; private set; } = null!;
    public string PluginCredentialsPath { get; private set; } = null!;
    public string PluginConfigPath { get; private set; } = null!;
    public string BackupsDirectoryPath { get; private set; } = null!;
    public string LogsDirectoryPath { get; private set; } = null!;
    public string MainConfigFilePath { get; private set; } = null!;
    public string DevicesFilePath { get; private set; } = null!;
    public string VariablesFilePath { get; private set; } = null!;
    public string ProfilesFilePath { get; private set; } = null!;

    public ApplicationPaths(bool portableMode)
    {
        _portableMode = portableMode;
        InitializePaths();
        CheckPaths();
    }
    
    private void InitializePaths()
    {
        ExecutablePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "Macro Deck 2.exe");
        MainDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
        UserDirectoryPath = _portableMode ?
            Path.Combine(MainDirectoryPath, "Data") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macro Deck");
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
        ProfilesFilePath = Path.Combine(UserDirectoryPath, "profiles.db");
    }


    private void CheckPaths()
    {
        MacroDeckLogger.Info("Checking paths...");
        bool CheckCreatePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (Directory.Exists(path)) return true;

            try
            {
                Directory.CreateDirectory(path);
                MacroDeckLogger.Info($"Created {path}");
                return true;
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error($"Failed to create {path}: {ex.Message}");
            }

            return false;
        }

        CheckCreatePath(UserDirectoryPath);
        CheckCreatePath(LogsDirectoryPath);
        CheckCreatePath(PluginCredentialsPath);
        CheckCreatePath(PluginConfigPath);
        CheckCreatePath(PluginsDirectoryPath);
        CheckCreatePath(BackupsDirectoryPath);
        CheckCreatePath(IconPackDirectoryPath);


        if (!CheckCreatePath(TempDirectoryPath)) return;
        {
            // Clean up temp directory
            DirectoryInfo di = new(TempDirectoryPath);
            CleanupTempDir(di);
        }

        MacroDeckLogger.Info("Checking paths done");

        void CleanupTempDir(DirectoryInfo directoryInfo)
        {
            if (!directoryInfo.Exists) return;
            foreach (var file in directoryInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning($"Failed to delete temp file: {ex.Message}");
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
                    MacroDeckLogger.Warning($"Failed to delete temp dir: {ex.Message}");
                }
            }
        }
    }
}