﻿using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        static string _manifestFileName = "Plugin.xml";
        static string _deleteMarkerFileName = ".delete";

        public static void Load()
        {
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
                            Debug.WriteLine(directory + " -> " + destinationDirectory);
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
                    if (File.Exists(directory + "\\" + _deleteMarkerFileName))
                    {
                        try
                        {
                            File.Delete(directory + "\\" + _deleteMarkerFileName);
                            Directory.Delete(directory, true);
                        } catch { }
                        continue;
                    }
                    var manifestFile = directory + "\\" + _manifestFileName;
                    if (!File.Exists(manifestFile)) continue;
                    try
                    {
                        PluginManifest pluginManifest = new PluginManifest();
                        using (Stream stream = new FileStream(manifestFile, FileMode.Open, FileAccess.Read))
                        {
                            pluginManifest = (PluginManifest)new XmlSerializer(typeof(PluginManifest)).Deserialize(stream);
                        }
                        Debug.WriteLine(pluginManifest.MainFile);
                        if (pluginManifest == null) continue;
                        var plugin = LoadPlugin(pluginManifest, directory);
                        plugin.Author = pluginManifest.Author;
                    } catch {}
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

        private static MacroDeckPlugin LoadPlugin(PluginManifest pluginManifest, string pluginDirectory, bool enable = false)
        {
            Assembly asm = null;
            try
            {
                asm = Assembly.LoadFrom(pluginDirectory + "\\" + pluginManifest.MainFile);
                Debug.WriteLine("[PluginManager] loading plugin " + asm.GetName().Name);

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
                            if (enable) plugin.Enable();
                            return plugin;
                        }
                    }
                    catch
                    {

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
                        Author = pluginManifest.Author,
                    };

                    PluginsNotLoaded[disabledPlugin.Name] = disabledPlugin;
                    PluginDirectories[disabledPlugin] = pluginDirectory;

                    MacroDeck.SafeMode = true;

                    using (var msgBox = new GUI.CustomControls.MessageBox())
                    {
                        Debug.WriteLine(ex.Message);
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

        private static void SearchUpdate(MacroDeckPlugin plugin)
        {
            if (UpdatedPlugins.Contains(plugin)) return;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?action=check-version&name=" + plugin.Name + "&version=" + plugin.Version + "&target-api=" + MacroDeck.PluginApiVersion);
                    JObject jsonObject = JObject.Parse(jsonString);
                    bool update = (bool)jsonObject["newer-version-available"];
                    Debug.WriteLine(plugin.Name + " update: " + update.ToString());
                    if (update)
                    {
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
            MacroDeckPlugin plugin = Plugins[name];
            if (ProtectedPlugins.Contains(plugin)) return;
            if (PluginDirectories.ContainsKey(plugin))
            {
                try
                {
                    if (UpdatedPlugins.Contains(Plugins[name]))
                    {
                        UpdatedPlugins.Remove(Plugins[name]);
                    }
                    if (PluginsNotLoaded.ContainsKey(name))
                    {
                        PluginsNotLoaded.Remove(name);
                    }
                    if (Plugins.ContainsKey(name))
                    {
                        if (PluginsUpdateAvailable.Contains(Plugins[name]))
                        {
                            PluginsUpdateAvailable.Remove(Plugins[name]);
                        }
                        Plugins.Remove(name);
                    }
                    //File.Delete(PluginsPaths[name]);
                    var deleteMarkerFile = PluginDirectories[plugin] + "\\" + _deleteMarkerFileName;
                    Debug.WriteLine(deleteMarkerFile);
                    File.Create(deleteMarkerFile);
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

        public static void InstallPlugin(string directory, string packageName, bool update, bool silent = false)
        {
            Assembly asm = null;
            bool error = false;
            PluginManifest pluginManifest = new PluginManifest();
            try
            {
                //File.Copy(filePath, MacroDeck.PluginsDirectoryPath + Path.GetFileName(filePath), true);
                //var pluginFile = MacroDeck.LoadedPluginsDirectoryPath + Path.GetFileName(filePath);
                //File.Copy(MacroDeck.PluginsDirectoryPath + Path.GetFileName(filePath), pluginFile, true);

                var installationDirectory = MacroDeck.PluginsDirectoryPath + packageName;
                if (update)
                {
                    installationDirectory = MacroDeck.UpdatePluginsDirectoryPath + packageName;
                }
                
                DirectoryCopy.Copy(directory, installationDirectory, true);

                if (!update)
                {
                    var manifestFile = directory + "\\" + _manifestFileName;
                    if (!File.Exists(manifestFile))
                    {
                        error = true;
                    }
                    else
                    {
                        try
                        {
                            using (Stream stream = new FileStream(manifestFile, FileMode.Open, FileAccess.Read))
                            {
                                pluginManifest = (PluginManifest)new XmlSerializer(typeof(PluginManifest)).Deserialize(stream);
                            }
                            Debug.WriteLine(pluginManifest.MainFile);
                            if (pluginManifest == null)
                            {
                                error = true;
                            }
                            else
                            {
                                var plugin = LoadPlugin(pluginManifest, installationDirectory);
                                plugin.Author = pluginManifest.Author;
                            }
                        }
                        catch
                        {
                            error = true;
                        }
                    }
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
                if (asm != null)
                {
                    var disabledPlugin = new DisabledPlugin
                    {
                        Name = asm.GetName().Name,
                        Version = asm.GetName().Version.ToString(),
                        Author = pluginManifest.Author,
                    };

                    PluginsNotLoaded[asm.GetName().Name] = disabledPlugin;

                    if (!silent)
                    {
                        using (var msgBox = new GUI.CustomControls.MessageBox())
                        {
                            msgBox.ShowDialog(Language.LanguageManager.Strings.ErrorWhileInstallingPlugin, String.Format(Language.LanguageManager.Strings.PluginXCouldNotBeInstalled, asm.GetName().Name), MessageBoxButtons.OK);
                        }
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
        public static void AddPlugin(MacroDeckPlugin macroDeckPlugin, bool protect = false)
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

        public static void EnablePlugins()
        {
            foreach (MacroDeckPlugin plugin in Plugins.Values)
            {
                plugin.Enable();
            }
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


    }
}
