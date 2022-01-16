using Newtonsoft.Json;
using SuchByte.MacroDeck.Backup;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SuchByte.MacroDeck.Backups
{
    public class BackupFailedEventArgs : EventArgs
    {
        public string Message;
    }


    public class BackupManager
    {
        private static bool BackupInProgress = false;

        public static event EventHandler BackupSaved;
        public static event EventHandler<BackupFailedEventArgs> BackupFailed;
        public static event EventHandler DeleteSuccess;
        
        
        public static List<MacroDeckBackupInfo> GetBackups()
        {
            List<MacroDeckBackupInfo> backups = new List<MacroDeckBackupInfo>();
            foreach (var filename in Directory.GetFiles(MacroDeck.BackupsDirectoryPath))
            {
                backups.Add(new MacroDeckBackupInfo()
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
            string restoreDirectory = Path.Combine(MacroDeck.TempDirectoryPath, "backup_restore");
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

            if (restoreBackupInfo.RestoreConfig && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.ConfigFilePath))))
            {
                try
                {
                    File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.ConfigFilePath)), MacroDeck.ConfigFilePath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (config) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestoreProfiles && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.ProfilesFilePath))))
            {
                try
                {
                    File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.ProfilesFilePath)), MacroDeck.ProfilesFilePath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (profiles) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestoreDevices && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.DevicesFilePath))))
            {
                try
                {
                    File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.DevicesFilePath)), MacroDeck.DevicesFilePath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (devices) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestoreVariables && File.Exists(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.VariablesFilePath))))
            {
                try
                {
                    File.Copy(Path.Combine(restoreDirectory, Path.GetFileName(MacroDeck.VariablesFilePath)), MacroDeck.VariablesFilePath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (variables) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestorePlugins && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginsDirectoryPath).Name)))
            {
                try
                {
                    if (Directory.Exists(MacroDeck.PluginsDirectoryPath))
                    {
                        Directory.Delete(MacroDeck.PluginsDirectoryPath, true);
                    }
                    DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginsDirectoryPath).Name), MacroDeck.PluginsDirectoryPath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (plugins) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestorePluginConfigs && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginConfigPath).Name)))
            {
                try
                {
                    if (Directory.Exists(MacroDeck.PluginConfigPath))
                    {
                        Directory.Delete(MacroDeck.PluginConfigPath, true);
                    }
                    DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginConfigPath).Name), MacroDeck.PluginConfigPath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (plugin configs) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestorePluginCredentials && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginCredentialsPath).Name)))
            {
                try
                {
                    if (Directory.Exists(MacroDeck.PluginCredentialsPath))
                    {
                        Directory.Delete(MacroDeck.PluginCredentialsPath, true);
                    }
                    DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.PluginCredentialsPath).Name), MacroDeck.PluginCredentialsPath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (plugin credentials) restore failed: " + ex.Message);
                }
            }
            if (restoreBackupInfo.RestoreIconPacks && Directory.Exists(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.IconPackDirectoryPath).Name)))
            {
                try
                {
                    if (Directory.Exists(MacroDeck.IconPackDirectoryPath))
                    {
                        Directory.Delete(MacroDeck.IconPackDirectoryPath, true);
                    }
                    DirectoryCopy.Copy(Path.Combine(restoreDirectory, new DirectoryInfo(MacroDeck.IconPackDirectoryPath).Name), MacroDeck.IconPackDirectoryPath, true);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup (icon packs) restore failed: " + ex.Message);
                }
            }
            MacroDeckLogger.Info("Backup successfully restored");
            using (var msgBox = new MessageBox())
            {
                msgBox.ShowDialog("Backup restored", "Backup successfully restored", System.Windows.Forms.MessageBoxButtons.OK);
            }
        }

        public static void RestoreBackup(string backupFileName, RestoreBackupInfo restoreBackupInfo)
        {
            string restoreDirectory = Path.Combine(MacroDeck.TempDirectoryPath, "backup_restore");
            try
            {
                if (!Directory.Exists(restoreDirectory))
                {
                    Directory.CreateDirectory(restoreDirectory);
                }
                else
                {
                    DirectoryInfo directory = new DirectoryInfo(restoreDirectory);
                    foreach (FileInfo file in directory.GetFiles()) file.Delete();
                    foreach (DirectoryInfo subDirectory in directory.GetDirectories()) subDirectory.Delete(true);
                }
            } catch { return; }

            try
            {
                ZipFile.ExtractToDirectory(Path.Combine(MacroDeck.BackupsDirectoryPath, backupFileName), restoreDirectory, true);

                JsonSerializer serializer = new JsonSerializer
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                };

                using (StreamWriter sw = new StreamWriter(Path.Combine(restoreDirectory, ".restore")))
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
            string backupFileName = string.Format("backup_{0}.zip", DateTime.Now.ToString("yy-MM-dd_HH-mm-ss"));
            MacroDeckLogger.Info("Sarting creation of backup: " + backupFileName);

            try
            {
                using (var archive = ZipFile.Open(Path.Combine(MacroDeck.BackupsDirectoryPath, backupFileName), ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(MacroDeck.ConfigFilePath, Path.GetFileName(MacroDeck.ConfigFilePath));
                    archive.CreateEntryFromFile(MacroDeck.ProfilesFilePath, Path.GetFileName(MacroDeck.ProfilesFilePath));
                    archive.CreateEntryFromFile(MacroDeck.DevicesFilePath, Path.GetFileName(MacroDeck.DevicesFilePath));
                    archive.CreateEntryFromFile(MacroDeck.VariablesFilePath, Path.GetFileName(MacroDeck.VariablesFilePath));
                    foreach (var directory in Directory.GetDirectories(MacroDeck.PluginsDirectoryPath))
                    {
                        DirectoryInfo pluginDirectoryInfo = new DirectoryInfo(directory);
                        foreach (FileInfo file in pluginDirectoryInfo.GetFiles("*"))
                        {
                            archive.CreateEntryFromFile(Path.Combine(MacroDeck.PluginsDirectoryPath, pluginDirectoryInfo.Name, file.Name), Path.Combine(new DirectoryInfo(MacroDeck.PluginsDirectoryPath).Name, pluginDirectoryInfo.Name, file.Name));
                        }
                    }
                    DirectoryInfo pluginConfigDirectoryInfo = new DirectoryInfo(MacroDeck.PluginConfigPath);
                    foreach (FileInfo file in pluginConfigDirectoryInfo.GetFiles("*"))
                    {
                        archive.CreateEntryFromFile(Path.Combine(MacroDeck.PluginConfigPath, file.Name), Path.Combine(pluginConfigDirectoryInfo.Name, file.Name));
                    }
                    DirectoryInfo pluginCredentialsDirectoryInfo = new DirectoryInfo(MacroDeck.PluginCredentialsPath);
                    foreach (FileInfo file in pluginCredentialsDirectoryInfo.GetFiles("*"))
                    {
                        archive.CreateEntryFromFile(Path.Combine(MacroDeck.PluginCredentialsPath, file.Name), Path.Combine(pluginCredentialsDirectoryInfo.Name, file.Name));
                    }
                    DirectoryInfo iconPackDirectoryInfo = new DirectoryInfo(MacroDeck.IconPackDirectoryPath);
                    foreach (FileInfo file in iconPackDirectoryInfo.GetFiles("*"))
                    {
                        archive.CreateEntryFromFile(Path.Combine(MacroDeck.IconPackDirectoryPath, file.Name), Path.Combine(iconPackDirectoryInfo.Name, file.Name));
                    }

                    MacroDeckLogger.Info("Backup successfully created: " + backupFileName);
                    if (BackupSaved != null)
                    {
                        BackupSaved(null, EventArgs.Empty);
                    }
                }
            } catch (Exception ex)
            {
                MacroDeckLogger.Error("Backup creation failed: " + ex.Message + Environment.NewLine + ex.StackTrace);
                if (BackupFailed != null)
                {
                    BackupFailed(null, new BackupFailedEventArgs() { Message = ex.Message });
                }
            } finally
            {
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
                    if (DeleteSuccess != null)
                    {
                        DeleteSuccess(null, EventArgs.Empty);
                    }
                } catch (Exception ex)
                {
                    MacroDeckLogger.Error("Backup deletion failed: " + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }
        }


    }
}
