using Newtonsoft.Json.Linq;
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


        public static Dictionary<string, IMacroDeckPlugin> Plugins = new Dictionary<string, IMacroDeckPlugin>();
        public static List<IMacroDeckPlugin> ProtectedPlugins = new List<IMacroDeckPlugin>();
        public static Dictionary<string, IMacroDeckPlugin> PluginsNotLoaded = new Dictionary<string, IMacroDeckPlugin>();
        public static List<IMacroDeckPlugin> PluginsUpdateAvailable = new List<IMacroDeckPlugin>();
        public static Dictionary<string, string> PluginsPaths = new Dictionary<string, string>();


        public static void Load()
        {
            Plugins.Clear();
            PluginsUpdateAvailable.Clear();
            PluginsNotLoaded.Clear();
            if (!Directory.Exists(MacroDeck.PluginsDirectoryPath))
            {
                Directory.CreateDirectory(MacroDeck.PluginsDirectoryPath);
            }
            else
            {
                if (!Directory.Exists(MacroDeck.LoadedPluginsDirectoryPath))
                {
                    Directory.CreateDirectory(MacroDeck.LoadedPluginsDirectoryPath);
                }
                else
                {
                    foreach (var dll in Directory.GetFiles(MacroDeck.LoadedPluginsDirectoryPath))
                    {
                        try
                        {
                            File.Delete(dll);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }

                foreach (var dll in Directory.GetFiles(MacroDeck.PluginsDirectoryPath, "*.dll"))
                {
                    var pluginFile = MacroDeck.LoadedPluginsDirectoryPath + Path.GetFileName(dll);
                    try
                    {
                        File.Copy(dll, pluginFile, true);
                    }
                    catch
                    {
                        continue;
                    }

                    Assembly asm = null;
                    try
                    {
                        asm = Assembly.LoadFrom(pluginFile);
                        Debug.WriteLine("[PluginManager] loading plugin " + asm.GetName().Name);

                        foreach (var type in asm.GetTypes())
                        {
                            try
                            {
                                if (type.GetInterface("IMacroDeckPlugin") == typeof(IMacroDeckPlugin))
                                {
                                    var plugin = Activator.CreateInstance(type) as IMacroDeckPlugin;
                                    AddPlugin(plugin);
                                    PluginsPaths[plugin.Name] = dll;
                                    Task.Run(() =>
                                        SearchUpdate(plugin)
                                    );
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
                                Version = asm.GetName().Version.ToString(),
                            };

                            PluginsNotLoaded[disabledPlugin.Name] = disabledPlugin;
                            PluginsPaths[disabledPlugin.Name] = dll;

                            MacroDeck.SafeMode = true;

                            using (var msgBox = new GUI.CustomControls.MessageBox())
                            {
                                Debug.WriteLine(ex.Message);
                                msgBox.ShowDialog(Language.LanguageManager.Strings.ErrorWhileLoadingPlugins, String.Format(Language.LanguageManager.Strings.PluginXCouldNotBeLoaded, asm.GetName().Name, asm.GetName().Version), MessageBoxButtons.OK);
                            }
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

        public static void ScanUpdatesAsync()
        {
            PluginsUpdateAvailable.Clear();
            Task.Run(() =>
            {
                foreach (IMacroDeckPlugin plugin in Plugins.Values)
                {
                    SearchUpdate(plugin);
                }
                foreach (IMacroDeckPlugin plugin in PluginsNotLoaded.Values)
                {
                    SearchUpdate(plugin);
                }
            });
        }

        private static void SearchUpdate(IMacroDeckPlugin plugin)
        {
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
                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
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
            if (PluginsPaths.ContainsKey(name))
            {
                try
                {
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
                    File.Delete(PluginsPaths[name]);
                    
                } catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message.ToString());
                }
            }
            ProtectedPlugins.RemoveAll(plugin => plugin.Name.Equals(name));
            if (OnPluginsChange != null)
            {
                OnPluginsChange(name, EventArgs.Empty);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public static void InstallPlugin(string filePath, bool silent = false)
        {
            Assembly asm = null;
            bool error = false;
            try
            {
                File.Copy(filePath, MacroDeck.PluginsDirectoryPath + Path.GetFileName(filePath), true);
                var pluginFile = MacroDeck.LoadedPluginsDirectoryPath + Path.GetFileName(filePath);
                File.Copy(MacroDeck.PluginsDirectoryPath + Path.GetFileName(filePath), pluginFile, true);

                try
                {
                    asm = Assembly.LoadFrom(pluginFile);

                    foreach (var type in asm.GetTypes())
                    {
                        try
                        {
                            if (type.GetInterface("IMacroDeckPlugin") == typeof(IMacroDeckPlugin))
                            {
                                var plugin = Activator.CreateInstance(type) as IMacroDeckPlugin;
                                AddPlugin(plugin);
                                plugin.Enable();

                                if (PluginsUpdateAvailable.Contains(plugin))
                                {
                                    PluginsUpdateAvailable.Remove(plugin);
                                }

                                if (OnPluginsChange != null)
                                {
                                    OnPluginsChange(plugin, EventArgs.Empty);
                                }

                                if (!silent)
                                {
                                    using (var msgBox = new GUI.CustomControls.MessageBox())
                                    {
                                        msgBox.ShowDialog(Language.LanguageManager.Strings.InstallationSuccessfull, String.Format(Language.LanguageManager.Strings.PluginXInstalledSuccessfully, asm.GetName().Name), MessageBoxButtons.OK);
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
                catch
                {
                    error = true;
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
        public static void AddPlugin(IMacroDeckPlugin macroDeckPlugin, bool protect = false)
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
            foreach (IMacroDeckPlugin plugin in Plugins.Values)
            {
                plugin.Enable();
            }
        }

        public static IMacroDeckAction GetActionByName(IMacroDeckPlugin plugin, string name)
        {
            IMacroDeckAction action = plugin.Actions.Find(plugin => plugin.Name.Equals(name));
            if (action == null) return null;
            using (var ms = new MemoryStream())
            {
                XmlSerializer serializer = new XmlSerializer(action.GetType());
                serializer.Serialize(ms, action);
                ms.Seek(0, SeekOrigin.Begin);
                return (IMacroDeckAction)serializer.Deserialize(ms);
            }
        }


    }
}
