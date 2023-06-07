using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Backup;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.Backups;

public class BackupFailedEventArgs : EventArgs
{
    public string Message;
}


public class BackupManager
{
    private static bool BackupInProgress;

    public static event EventHandler BackupSaved;
    public static event EventHandler<BackupFailedEventArgs> BackupFailed;
    public static event EventHandler DeleteSuccess;
        
        
    public static List<MacroDeckBackupInfo> GetBackups()
    {
        var backups = new List<MacroDeckBackupInfo>();
        foreach (var filename in Directory.GetFiles(ApplicationPaths.BackupsDirectoryPath))
        {
            backups.Add(new MacroDeckBackupInfo
            {
                BackupCreated = File.GetCreationTime(filename),
                FileName = filename,
                SizeMb = new FileInfo(filename).Length / 1048576.0f
            });
        }
        return backups.OrderByDescending(x => x.BackupCreated).ToList();
    }

    public static void CheckRestoreDirectory()
    {
        var restoreDirectory = Path.Combine(ApplicationPaths.TempDirectoryPath, "backup_restore");
        if (!Directory.Exists(restoreDirectory)) return;
        if (!File.Exists(Path.Combine(restoreDirectory, ".restore"))) return;
        RestoreBackupInfo restoreBackupInfo;
        try
        {
            restoreBackupInfo = JsonConvert.DeserializeObject<RestoreBackupInfo>(File.ReadAllText(Path.Combine(restoreDirectory, ".restore")), new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            });
            File.Delete(Path.Combine(restoreDirectory, ".restore"));
        } catch { return; }

        if (restoreBackupInfo.RestoreConfig && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.MainConfigFilePath))))
        {
            try
            {
                File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.MainConfigFilePath)), ApplicationPaths.MainConfigFilePath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (config) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestoreProfiles && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.ProfilesFilePath))))
        {
            try
            {
                File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.ProfilesFilePath)), ApplicationPaths.ProfilesFilePath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (profiles) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestoreDevices && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.DevicesFilePath))))
        {
            try
            {
                File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.DevicesFilePath)), ApplicationPaths.DevicesFilePath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (devices) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestoreVariables && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.VariablesFilePath))))
        {
            try
            {
                File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(ApplicationPaths.VariablesFilePath)), ApplicationPaths.VariablesFilePath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (variables) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestorePlugins && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginsDirectoryPath).Name)))
        {
            try
            {
                if (Directory.Exists(ApplicationPaths.PluginsDirectoryPath))
                {
                    Directory.Delete(ApplicationPaths.PluginsDirectoryPath, true);
                }
                DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginsDirectoryPath).Name), ApplicationPaths.PluginsDirectoryPath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (plugins) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestorePluginConfigs && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginConfigPath).Name)))
        {
            try
            {
                if (Directory.Exists(ApplicationPaths.PluginConfigPath))
                {
                    Directory.Delete(ApplicationPaths.PluginConfigPath, true);
                }
                DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginConfigPath).Name), ApplicationPaths.PluginConfigPath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (plugin configs) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestorePluginCredentials && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginCredentialsPath).Name)))
        {
            try
            {
                if (Directory.Exists(ApplicationPaths.PluginCredentialsPath))
                {
                    Directory.Delete(ApplicationPaths.PluginCredentialsPath, true);
                }
                DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.PluginCredentialsPath).Name), ApplicationPaths.PluginCredentialsPath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (plugin credentials) restore failed: " + ex.Message);
            }
        }
        if (restoreBackupInfo.RestoreIconPacks && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.IconPackDirectoryPath).Name)))
        {
            try
            {
                if (Directory.Exists(ApplicationPaths.IconPackDirectoryPath))
                {
                    Directory.Delete(ApplicationPaths.IconPackDirectoryPath, true);
                }
                DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(ApplicationPaths.IconPackDirectoryPath).Name), ApplicationPaths.IconPackDirectoryPath, true);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup (icon packs) restore failed: " + ex.Message);
            }
        }
        MacroDeckLogger.Info("Backup successfully restored");
        using var msgBox = new MessageBox();
        msgBox.ShowDialog("Backup restored", "Backup successfully restored", MessageBoxButtons.OK);
    }

    public static void RestoreBackup(string backupFileName, RestoreBackupInfo restoreBackupInfo)
    {
        var restoreDirectory = Path.Combine(ApplicationPaths.TempDirectoryPath, "backup_restore");
        try
        {
            if (!Directory.Exists(restoreDirectory))
            {
                Directory.CreateDirectory(restoreDirectory);
            }
            else
            {
                var directory = new DirectoryInfo(restoreDirectory);
                foreach (var file in directory.GetFiles()) file.Delete();
                foreach (var subDirectory in directory.GetDirectories()) subDirectory.Delete(true);
            }
        } catch { return; }

        try
        {
            ZipFile.ExtractToDirectory(Path.Combine(ApplicationPaths.BackupsDirectoryPath, backupFileName), restoreDirectory, true);

            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            using (var sw = new StreamWriter(Path.Combine(restoreDirectory, ".restore")))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, restoreBackupInfo);
            }

            MacroDeck.RestartMacroDeck("--show");
        } catch (Exception ex) {

            MacroDeckLogger.Error("Backup restoration failed: " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
    }


    public static void CreateBackup()
    {
        if (BackupInProgress) return;
        BackupInProgress = true;
        VariableManager.Close();
        var backupFileName = $"backup_{DateTime.Now:yy-MM-dd_HH-mm-ss}.zip";
        MacroDeckLogger.Info("Starting creation of backup: " + backupFileName);

        try
        {
            using var archive = ZipFile.Open(Path.Combine(ApplicationPaths.BackupsDirectoryPath, backupFileName), ZipArchiveMode.Create);
            archive.CreateEntryFromFile(ApplicationPaths.MainConfigFilePath, Path.GetFileName(ApplicationPaths.MainConfigFilePath));
            archive.CreateEntryFromFile(ApplicationPaths.ProfilesFilePath, Path.GetFileName(ApplicationPaths.ProfilesFilePath));
            archive.CreateEntryFromFile(ApplicationPaths.DevicesFilePath, Path.GetFileName(ApplicationPaths.DevicesFilePath));
            archive.CreateEntryFromFile(ApplicationPaths.VariablesFilePath, Path.GetFileName(ApplicationPaths.VariablesFilePath));
            foreach (var directory in Directory.GetDirectories(ApplicationPaths.PluginsDirectoryPath))
            {
                var pluginDirectoryInfo = new DirectoryInfo(directory);
                foreach (var file in pluginDirectoryInfo.GetFiles("*"))
                {
                    archive.CreateEntryFromFile(Path.Combine(ApplicationPaths.PluginsDirectoryPath, pluginDirectoryInfo.Name, file.Name), Path.Combine(new DirectoryInfo(ApplicationPaths.PluginsDirectoryPath).Name, pluginDirectoryInfo.Name, file.Name));
                }
            }
            var pluginConfigDirectoryInfo = new DirectoryInfo(ApplicationPaths.PluginConfigPath);
            foreach (var file in pluginConfigDirectoryInfo.GetFiles("*"))
            {
                archive.CreateEntryFromFile(Path.Combine(ApplicationPaths.PluginConfigPath, file.Name), Path.Combine(pluginConfigDirectoryInfo.Name, file.Name));
            }
            var pluginCredentialsDirectoryInfo = new DirectoryInfo(ApplicationPaths.PluginCredentialsPath);
            foreach (var file in pluginCredentialsDirectoryInfo.GetFiles("*"))
            {
                archive.CreateEntryFromFile(Path.Combine(ApplicationPaths.PluginCredentialsPath, file.Name), Path.Combine(pluginCredentialsDirectoryInfo.Name, file.Name));
            }
            var iconPackDirectoryInfo = new DirectoryInfo(ApplicationPaths.IconPackDirectoryPath);
            foreach (var dir in iconPackDirectoryInfo.GetDirectories())
            {
                foreach (var iconPackFile in dir.GetFiles())
                {
                    archive.CreateEntryFromFile(Path.Combine(ApplicationPaths.IconPackDirectoryPath, dir.Name, iconPackFile.Name), Path.Combine(iconPackDirectoryInfo.Name, dir.Name, iconPackFile.Name));
                }
            }

            MacroDeckLogger.Info("Backup successfully created: " + backupFileName);
            BackupSaved?.Invoke(null, EventArgs.Empty);
        } catch (Exception ex)
        {
            MacroDeckLogger.Error("Backup creation failed: " + ex.Message + Environment.NewLine + ex.StackTrace);
            BackupFailed?.Invoke(null, new BackupFailedEventArgs { Message = ex.Message });
        } finally
        {
            VariableManager.Initialize();
            BackupInProgress = false;
        }
    }

    public static void DeleteBackup(string fileName)
    {
        if (File.Exists(fileName))
        {
            try
            {
                File.Delete(fileName);
                MacroDeckLogger.Info("Backup successfully deleted: " + fileName);
                DeleteSuccess?.Invoke(null, EventArgs.Empty);
            } catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup deletion failed: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
    }
}