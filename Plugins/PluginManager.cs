using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.Plugins
{
    public static class PluginManager
    {
        public static event EventHandler OnPluginsChange;
        public static event EventHandler OnUpdateCheckFinished;


        public static Dictionary<string, MacroDeckPlugin> Plugins = new Dictionary<string, MacroDeckPlugin>();
        public static List<MacroDeckPlugin> ProtectedPlugins = new List<MacroDeckPlugin>();
        public static List<MacroDeckPlugin> UpdatedPlugins = new List<MacroDeckPlugin>();
        public static Dictionary<string, MacroDeckPlugin> PluginsNotLoaded = new Dictionary<string, MacroDeckPlugin>();
        public static List<MacroDeckPlugin> PluginsUpdateAvailable = new List<MacroDeckPlugin>();
        public static Dictionary<MacroDeckPlugin, string> PluginDirectories = new Dictionary<MacroDeckPlugin, string>();

        static string _manifestFileName = "ExtensionManifest.json";
        static string _manifestFileNameLegacy = "Plugin.xml";
        static string _deleteMarkerFileName = ".delete";

        static bool _loaded = false;

        public static void Load()
        {
            if (_loaded) return;
            MacroDeckLogger.Info(typeof(PluginManager), "Loading plugins...");
            _loaded = true;
            Plugins.Clear();
            PluginsUpdateAvailable.Clear();
            PluginsNotLoaded.Clear();
            ProtectedPlugins.Clear();
            PluginDirectories.Clear();
            if (!Directory.Exists(MacroDeck.PluginsDirectoryPath))
            {
                Directory.CreateDirectory(MacroDeck.PluginsDirectoryPath);
            }

            // Delete the deprecated .loaded directory if it exists
            if (Directory.Exists(MacroDeck.PluginsDirectoryPath + ".loaded"))
            {
                try
                {
                    Directory.Delete(MacroDeck.PluginsDirectoryPath + ".loaded", true);
                } catch { }
            }
            else
            {
                // Load updates from the .updates directory
                if (Directory.Exists(MacroDeck.UpdatePluginsDirectoryPath))
                {
                    foreach (var directory in Directory.GetDirectories(MacroDeck.UpdatePluginsDirectoryPath))
                    {
                        try
                        {
                            var destinationDirectory = Path.Combine(MacroDeck.PluginsDirectoryPath, new DirectoryInfo(directory).Name);
                            DirectoryCopy.Copy(directory, destinationDirectory, true);
                            Directory.Delete(directory, true);
                        } catch { }
                    }
                    try
                    {
                        Directory.Delete(MacroDeck.UpdatePluginsDirectoryPath, true);
                    } catch { }
                }

                // Load the plugins
                foreach (var directory in Directory.GetDirectories(MacroDeck.PluginsDirectoryPath))
                {
                    // Delete plugin if file ".delete" exists
                    if (File.Exists(Path.Combine(directory, _deleteMarkerFileName)))
                    {
                        try
                        {
                            File.Delete(Path.Combine(directory, _deleteMarkerFileName));
                            Directory.Delete(directory, true);
                        } catch { }
                        continue;
                    }
                    var manifestFile = Path.Combine(directory, _manifestFileName);
                    var manifestFileLegacy = Path.Combine(directory, _manifestFileNameLegacy);
                    if (!File.Exists(manifestFile) && !File.Exists(manifestFileLegacy))
                    {
                        continue;
                    }

                    if (File.Exists(manifestFile))
                    {
                        try
                        {
                            ExtensionManifestModel extensionManifest = ExtensionManifestModel.FromManifestFile(manifestFile);
                            if (extensionManifest == null) continue;
                            var plugin = LoadPlugin(extensionManifest, directory);
                            plugin.Author = extensionManifest.Author;
                        }
                        catch (Exception ex)
                        {
                            MacroDeckLogger.Error(typeof(PluginManager), $"Error while deserializing manifest for {directory}: {ex.Message}");
                        }
                    } else if (File.Exists(manifestFileLegacy))
                    {
                        try
                        {
                            PluginManifest pluginManifest = PluginManifest.FromManifestFile(manifestFileLegacy);
                            if (pluginManifest == null) continue;
                            var plugin = LoadLegacyPlugin(pluginManifest, directory);
                            plugin.Author = pluginManifest.Author;
                        }
                        catch (Exception ex)
                        {
                            MacroDeckLogger.Error(typeof(PluginManager), $"Error while deserializing legacy manifest for {directory}: {ex.Message}");
                        }
                    }
                }

            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Add internal plugins
            AddPlugin(new ActionButton.ActionButtonPlugin(), true);
            AddPlugin(new Variables.Plugin.VariablesPlugin(), true);
            AddPlugin(new Folders.Plugin.FolderPlugin(), true);
        }

        private static MacroDeckPlugin LoadPlugin(ExtensionManifestModel extensionManifest, string pluginDirectory, bool enable = false)
        {
            Assembly asm = null;
            try
            {
                asm = Assembly.LoadFrom(Path.Combine(pluginDirectory, extensionManifest.Dll));
                MacroDeckLogger.Info("Loading plugin " + asm.GetName().Name);

                foreach (var type in asm.GetTypes())
                {
                    try
                    {
                        if (type.IsClass && type.IsSubclassOf(typeof(MacroDeckPlugin)))
                        {
                            var plugin = Activator.CreateInstance(type) as MacroDeckPlugin;
                            AddPlugin(plugin);
                            PluginDirectories[plugin] = pluginDirectory;
                            Task.Run(() =>
                                SearchUpdate(plugin)
                            );
                            plugin.Author = extensionManifest.Author;
                            if (enable) plugin.Enable();
                            return plugin;
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error("Error while loading plugin: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Error while loading plugin: " + ex.Message);
                if (asm != null)
                {
                    var disabledPlugin = new DisabledPlugin
                    {
                        Name = asm.GetName().Name,
                        Version = FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion,
                        Author = extensionManifest.Author,
                    };

                    PluginsNotLoaded[disabledPlugin.Name] = disabledPlugin;
                    PluginDirectories[disabledPlugin] = pluginDirectory;

                    MacroDeck.SafeMode = true;

                    using (var msgBox = new GUI.CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.ErrorWhileLoadingPlugins, String.Format(Language.LanguageManager.Strings.PluginXCouldNotBeLoaded, disabledPlugin.Name, disabledPlugin.Version), MessageBoxButtons.OK);
                    }
                    return disabledPlugin;
                }
            }
            return null;
        }

        [Obsolete("Will be removed in version 2.11.0")]
        private static MacroDeckPlugin LoadLegacyPlugin(PluginManifest pluginManifest, string pluginDirectory, bool enable = false)
        {
            Assembly asm = null;
            try
            {
                asm = Assembly.LoadFrom(Path.Combine(pluginDirectory, pluginManifest.MainFile));
                MacroDeckLogger.Info("Loading legacy plugin " + asm.GetName().Name);

                foreach (var type in asm.GetTypes())
                {
                    try
                    {
                        if (type.IsClass && type.IsSubclassOf(typeof(MacroDeckPlugin)))
                        {
                            var plugin = Activator.CreateInstance(type) as MacroDeckPlugin;
                            AddPlugin(plugin);
                            PluginDirectories[plugin] = pluginDirectory;
                            Task.Run(() =>
                                SearchUpdate(plugin)
                            );
                            plugin.Author = pluginManifest.Author;
                            if (enable) plugin.Enable();
                            return plugin;
                        }
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error("Error while loading legacy plugin: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Error while loading legacy plugin: " + ex.Message);
                if (asm != null)
                {
                    var disabledPlugin = new DisabledPlugin
                    {
                        Name = asm.GetName().Name,
                        Version = FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion,
                        Author = pluginManifest.Author,
                    };

                    PluginsNotLoaded[disabledPlugin.Name] = disabledPlugin;
                    PluginDirectories[disabledPlugin] = pluginDirectory;

                    MacroDeck.SafeMode = true;

                    using (var msgBox = new GUI.CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.ErrorWhileLoadingPlugins, String.Format(Language.LanguageManager.Strings.PluginXCouldNotBeLoaded, disabledPlugin.Name, disabledPlugin.Version), MessageBoxButtons.OK);
                    }
                    return disabledPlugin;
                }
            }
            return null;
        }

        public static void ScanUpdatesAsync()
        {
            PluginsUpdateAvailable.Clear();
            Task.Run(() =>
            {
                foreach (MacroDeckPlugin plugin in Plugins.Values)
                {
                    SearchUpdate(plugin);
                }
                foreach (MacroDeckPlugin plugin in PluginsNotLoaded.Values)
                {
                    SearchUpdate(plugin);
                }
                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
                }
            });
        }

        internal static void SearchUpdate(MacroDeckPlugin plugin)
        {
            if (UpdatedPlugins.Contains(plugin)) return;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var jsonString = wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=check-update&package-id={ExtensionStoreHelper.GetPackageId(plugin)}&installed-version={plugin.Version}&target-api={MacroDeck.PluginApiVersion}");
                    JObject jsonObject = JObject.Parse(jsonString);
                    bool update = (bool)jsonObject["update-available"];
                    if (update)
                    {
                        MacroDeckLogger.Info("Update available for " + plugin.Name);
                        PluginsUpdateAvailable.Add(plugin);
                    }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        public static bool IsInstalled(string name)
        {
            return Plugins.ContainsKey(name);
        }
        public static void DeletePlugin(string name)
        {
            if (!Plugins.ContainsKey(name)) return;
            MacroDeckPlugin plugin = null;
            if (Plugins.ContainsKey(name))
            {
                plugin = Plugins[name];
            } else if (PluginsNotLoaded.ContainsKey(name))
            {
                plugin = PluginsNotLoaded[name];
            }
            if (plugin == null) return;
            if (ProtectedPlugins.Contains(plugin)) return;
            if (PluginDirectories.ContainsKey(plugin))
            {
                try
                {
                    if (UpdatedPlugins.Contains(plugin))
                    {
                        UpdatedPlugins.Remove(plugin);
                    }
                    if (PluginsNotLoaded.ContainsKey(name))
                    {
                        PluginsNotLoaded.Remove(name);
                    }
                    if (PluginsUpdateAvailable.Contains(plugin))
                    {
                        PluginsUpdateAvailable.Remove(plugin);
                    }
                    if (Plugins.ContainsKey(name))
                    {
                        Plugins.Remove(name);
                    }
                    var deleteMarkerFile = Path.Combine(PluginDirectories[plugin], _deleteMarkerFileName);
                    File.Create(deleteMarkerFile);
                    MacroDeckLogger.Info(name + " deleted");
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message.ToString());
                }
            }

            if (OnPluginsChange != null)
            {
                OnPluginsChange(name, EventArgs.Empty);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        internal static void InstallPluginFromZip(string zipFilePath)
        {
            var extensionManifest = ExtensionManifestModel.FromZipFilePath(zipFilePath);
            var extractedDirectory = Path.Combine(MacroDeck.TempDirectoryPath, extensionManifest.PackageId);
            ZipFile.ExtractToDirectory(Path.Combine(MacroDeck.TempDirectoryPath, zipFilePath), extractedDirectory, true);

            PluginManager.InstallPlugin(extractedDirectory, extensionManifest.PackageId);
        }

        internal static void InstallPlugin(string directory, string packageName)
        {
            bool update = Directory.Exists(Path.Combine(MacroDeck.PluginsDirectoryPath, packageName));
            MacroDeckLogger.Info(typeof(PluginManager), $"{(update ? "Updating" : "Installing")} " + packageName);
            Assembly asm = null;
            bool error = false;
            ExtensionManifestModel extensionManifest = new ExtensionManifestModel();
            try
            {
                var installationDirectory = Path.Combine(MacroDeck.PluginsDirectoryPath, packageName);
                if (update)
                {
                    installationDirectory = Path.Combine(MacroDeck.UpdatePluginsDirectoryPath, packageName);
                }

                DirectoryCopy.Copy(directory, installationDirectory, true);

                if (!update)
                {
                    var manifestFile = Path.Combine(installationDirectory, _manifestFileName);
                    if (!File.Exists(manifestFile))
                    {
                        error = true;
                    }
                    else
                    {
                        try
                        {
                            extensionManifest = ExtensionManifestModel.FromManifestFile(manifestFile);
                            if (extensionManifest == null)
                            {
                                error = true;
                            }
                            else
                            {
                                var plugin = LoadPlugin(extensionManifest, installationDirectory, true);

                                if (plugin != null && plugin.CanConfigure)
                                {
                                    using (var msgBox = new GUI.CustomControls.MessageBox())
                                    {
                                        if (msgBox.ShowDialog(Language.LanguageManager.Strings.PluginNeedsConfiguration, String.Format(Language.LanguageManager.Strings.ConfigureNow, plugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                        {
                                            plugin.OpenConfigurator();
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            error = true;
                        }
                    }
                }
                else
                {
                    UpdatedPlugins.Add(PluginDirectories.FirstOrDefault(x => x.Value == Path.Combine(MacroDeck.PluginsDirectoryPath, packageName)).Key);
                }

                if (OnPluginsChange != null)
                {
                    OnPluginsChange(null, EventArgs.Empty);
                }
            } catch
            {
                error = true;
            }
            if (error)
            {
                if (asm != null && extensionManifest != null)
                {
                    var disabledPlugin = new DisabledPlugin
                    {
                        Name = asm.GetName().Name,
                        Version = asm.GetName().Version.ToString(),
                        Author = extensionManifest.Author,
                    };

                    PluginsNotLoaded[asm.GetName().Name] = disabledPlugin;

                    using (var msgBox = new GUI.CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.ErrorWhileInstallingPlugin, String.Format(Language.LanguageManager.Strings.PluginXCouldNotBeInstalled, asm.GetName().Name), MessageBoxButtons.OK);
                    }
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Adds the plugin to the plugin list
        /// </summary>
        /// <param name="macroDeckPlugin">Instance of the plugin</param>
        /// <param name="protect">true = plugin cannot be uninstalled by the user</param>
        internal static void AddPlugin(MacroDeckPlugin macroDeckPlugin, bool protect = false)
        {
            if (!Plugins.ContainsKey(macroDeckPlugin.Name))
            {
                Plugins[macroDeckPlugin.Name] = macroDeckPlugin;
                if (protect)
                {
                    ProtectedPlugins.Add(macroDeckPlugin);
                }
            }
            if (OnPluginsChange != null)
            {
                OnPluginsChange(macroDeckPlugin, EventArgs.Empty);
            }
        }

        internal static void EnablePlugins()
        {
            MacroDeckLogger.Info(typeof(PluginManager), "Enabling plugins...");
            foreach (MacroDeckPlugin plugin in Plugins.Values)
            {
                if (plugin == null) continue;
                long enalingStarted = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                plugin.Enable();
                long enablingTook = DateTimeOffset.Now.ToUnixTimeMilliseconds() - enalingStarted;
                MacroDeckLogger.Info($"{plugin.Name} version {plugin.Version} enabled (took {enablingTook}ms)");
            }
            MacroDeckLogger.Info(typeof(PluginManager), $"Plugins enabled");
        }

        public static PluginAction GetActionByName(MacroDeckPlugin plugin, string name)
        {
            PluginAction action = plugin.Actions.Find(plugin => plugin.Name.Equals(name));
            if (action == null) return null;
            using (var ms = new MemoryStream())
            {
                XmlSerializer serializer = new XmlSerializer(action.GetType());
                serializer.Serialize(ms, action);
                ms.Seek(0, SeekOrigin.Begin);
                return (PluginAction)serializer.Deserialize(ms);
            }
        }

        public static PluginAction GetNewActionInstance(PluginAction action)
        {
            if (action == null) return null;
            using (var ms = new MemoryStream())
            {
                XmlSerializer serializer = new XmlSerializer(action.GetType());
                serializer.Serialize(ms, action);
                ms.Seek(0, SeekOrigin.Begin);
                return (PluginAction)serializer.Deserialize(ms);
            }
        }
        
        public static MacroDeckPlugin GetPluginByAction(PluginAction pluginAction)
        {
            foreach (MacroDeckPlugin macroDeckPlugin in Plugins.Values)
            {
                if (macroDeckPlugin.Actions.Find(mdp => mdp.GetType().FullName.Equals(pluginAction.GetType().FullName)) != null)
                {
                    return macroDeckPlugin;
                }
            }
            return  new DisabledPlugin()
            {
                Name = "Plugin not available",
            };
        }

        public static MacroDeckPlugin GetPluginByName(string name)
        {
            foreach (MacroDeckPlugin macroDeckPlugin in Plugins.Values)
            {
                if (macroDeckPlugin.Name.Equals(name))
                {
                    return macroDeckPlugin;
                }
            }
            return new DisabledPlugin()
            {
                Name = "Plugin not available",
            };
        }

    }
}
