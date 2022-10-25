﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Folders.Plugin;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables.Plugin;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.Plugins
{
    public static class PluginManager
    {
        public static event EventHandler OnPluginsChange;


        public static Dictionary<string, MacroDeckPlugin> Plugins = new();
        public static List<MacroDeckPlugin> ProtectedPlugins = new();
        public static List<MacroDeckPlugin> UpdatedPlugins = new();
        public static Dictionary<string, MacroDeckPlugin> PluginsNotLoaded = new();
        public static List<MacroDeckPlugin> PluginsUpdateAvailable = new();
        public static Dictionary<MacroDeckPlugin, string> PluginDirectories = new();

        private const string ManifestFileName = "ExtensionManifest.json";
        private const string DeleteMarkerFileName = ".delete";

        private static bool _loaded;

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
                    if (File.Exists(Path.Combine(directory, DeleteMarkerFileName)))
                    {
                        try
                        {
                            File.Delete(Path.Combine(directory, DeleteMarkerFileName));
                            Directory.Delete(directory, true);
                        } catch { }
                        continue;
                    }
                    var manifestFile = Path.Combine(directory, ManifestFileName);
                    if (!File.Exists(manifestFile))
                    {
                        continue;
                    }

                    try
                    {
                        var extensionManifest = ExtensionManifestModel.FromManifestFile(manifestFile);
                        if (extensionManifest == null) continue;
                        var plugin = LoadPlugin(extensionManifest, directory);
                        plugin.Author = extensionManifest.Author;
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error(typeof(PluginManager), $"Error while deserializing manifest for {directory}: {ex.Message}");
                    }
                }

            }

            // Add internal plugins
            AddPlugin(new ActionButtonPlugin(), true);
            AddPlugin(new VariablesPlugin(), true);
            AddPlugin(new FolderPlugin(), true);
            AddPlugin(new DevicePlugin(), true);
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
                            if (File.Exists(Path.Combine(pluginDirectory, "ExtensionIcon.png")))
                            {
                                plugin.PluginIcon = (Image)Image.FromFile(Path.Combine(pluginDirectory, "ExtensionIcon.png")).Clone();
                            }
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
                    MacroDeckLogger.Warning($"Cannot load {disabledPlugin.Name} version {disabledPlugin.Version}. Macro Deck was started in safe mode.");
                    return disabledPlugin;
                }
            }
            return null;
        }
       
        internal static void SearchUpdate(MacroDeckPlugin plugin)
        {
            if (UpdatedPlugins.Contains(plugin) || ProtectedPlugins.Contains(plugin)) return;
            try
            {
                using var wc = new WebClient();
                var jsonString = wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=check-update&package-id={ExtensionStoreHelper.GetPackageId(plugin)}&installed-version={plugin.Version}&target-api={MacroDeck.PluginApiVersion}");
                var jsonObject = JObject.Parse(jsonString);
                var update = (bool)jsonObject["update-available"];
                if (!update) return;
                MacroDeckLogger.Info("Update available for " + plugin.Name);
                PluginsUpdateAvailable.Add(plugin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
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
                    var deleteMarkerFile = Path.Combine(PluginDirectories[plugin], DeleteMarkerFileName);
                    File.Create(deleteMarkerFile);
                    MacroDeckLogger.Info(name + " deleted");
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message);
                }
            }

            OnPluginsChange?.Invoke(name, EventArgs.Empty);
        }

        internal static void InstallPluginFromZip(string zipFilePath)
        {
            var extensionManifest = ExtensionManifestModel.FromZipFilePath(zipFilePath);
            var extractedDirectory = Path.Combine(MacroDeck.TempDirectoryPath, extensionManifest.PackageId);
            ZipFile.ExtractToDirectory(Path.Combine(MacroDeck.TempDirectoryPath, zipFilePath), extractedDirectory, true);

            InstallPlugin(extractedDirectory, extensionManifest.PackageId);
        }

        internal static void InstallPlugin(string directory, string packageName)
        {
            var update = Directory.Exists(Path.Combine(MacroDeck.PluginsDirectoryPath, packageName));
            MacroDeckLogger.Info(typeof(PluginManager), $"{(update ? "Updating" : "Installing")} " + packageName);
            Assembly asm = null;
            var error = false;
            var extensionManifest = new ExtensionManifestModel();
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
                    var manifestFile = Path.Combine(installationDirectory, ManifestFileName);
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
                                try
                                {
                                    using (var wc = new WebClient())
                                    {
                                        wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=count-download&package-id={extensionManifest.PackageId}");
                                    }
                                } catch { }
                              
                                var plugin = LoadPlugin(extensionManifest, installationDirectory, true);

                                try
                                {
                                    if (plugin != null && plugin.CanConfigure)
                                    {
                                        using (var msgBox = new MessageBox())
                                        {
                                            if (msgBox.ShowDialog(LanguageManager.Strings.PluginNeedsConfiguration, string.Format(LanguageManager.Strings.ConfigureNow, plugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                                            {
                                                plugin.OpenConfigurator();
                                            }
                                        }
                                    }
                                } catch { }
                                
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

                OnPluginsChange?.Invoke(null, EventArgs.Empty);
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

                    using (var msgBox = new MessageBox())
                    {
                        msgBox.ShowDialog(LanguageManager.Strings.ErrorWhileInstallingPlugin, string.Format(LanguageManager.Strings.PluginXCouldNotBeInstalled, asm.GetName().Name), MessageBoxButtons.OK);
                    }
                }
            }
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
            OnPluginsChange?.Invoke(macroDeckPlugin, EventArgs.Empty);
        }

        internal static void EnablePlugins()
        {
            MacroDeckLogger.Info(typeof(PluginManager), "Enabling plugins...");
            foreach (var plugin in Plugins.Values)
            {
                if (plugin == null) continue;
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                try
                {
                    plugin.Enable();
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning($"Error while enabling {plugin.Name}: {ex.Message}");
                }
                stopWatch.Stop();
                var enablingTook = stopWatch.Elapsed.TotalMilliseconds;
                MacroDeckLogger.Info($"{plugin.Name} version {plugin.Version} enabled (took {enablingTook}ms)");
            }
            MacroDeckLogger.Info(typeof(PluginManager), "Plugins enabled");
        }

        public static PluginAction GetActionByName(MacroDeckPlugin plugin, string name)
        {
            var action = plugin.Actions.Find(plugin => plugin.Name.Equals(name));
            if (action == null) return null;
            using (var ms = new MemoryStream())
            {
                var serializer = new XmlSerializer(action.GetType());
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
                var serializer = new XmlSerializer(action.GetType());
                serializer.Serialize(ms, action);
                ms.Seek(0, SeekOrigin.Begin);
                return (PluginAction)serializer.Deserialize(ms);
            }
        }
        
        public static MacroDeckPlugin GetPluginByAction(PluginAction pluginAction)
        {
            foreach (var macroDeckPlugin in Plugins.Values)
            {
                if (macroDeckPlugin.Actions.Find(mdp => mdp.GetType().FullName.Equals(pluginAction.GetType().FullName)) != null)
                {
                    return macroDeckPlugin;
                }
            }
            return  new DisabledPlugin
            {
                Name = "Plugin not available",
            };
        }

        public static MacroDeckPlugin GetPluginByName(string name)
        {
            foreach (var macroDeckPlugin in Plugins.Values)
            {
                if (macroDeckPlugin.Name.Equals(name))
                {
                    return macroDeckPlugin;
                }
            }
            return new DisabledPlugin
            {
                Name = "Plugin not available",
            };
        }

    }
}
