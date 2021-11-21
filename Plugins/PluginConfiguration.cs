using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SuchByte.MacroDeck.Plugins
{
    public class PluginConfiguration
    {
        private static Dictionary<string, Dictionary<string, string>> _pluginConfigs = new Dictionary<string, Dictionary<string, string>>();

        public static void SetValue(MacroDeckPlugin plugin, string key, string value)
        {
            Dictionary<string, string> pluginConfig;

            if (!File.Exists(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower()))
            {
                pluginConfig = new Dictionary<string, string>();
            }
            else
            {
                pluginConfig = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json"), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                });
            }

            pluginConfig[key] = value;

            _pluginConfigs[plugin.Author + "." + plugin.Name] = pluginConfig;

            JsonSerializer serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            try
            {
                using (StreamWriter sw = new StreamWriter(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json"))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, pluginConfig);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        public static string GetValue(MacroDeckPlugin plugin = null, string key = "")
        {
            string value = "";
            try
            {
                if (plugin == null || key == null || _pluginConfigs == null) return value;
                if (!_pluginConfigs.ContainsKey(plugin.Author + "." + plugin.Name))
                {
                    Dictionary<string, string> pluginConfig = LoadPluginConfig(plugin);
                    if (pluginConfig == null || pluginConfig.Count == 0) return value;
                    _pluginConfigs.Add(plugin.Author + "." + plugin.Name, pluginConfig);
                    value = pluginConfig[key];
                } else
                {
                    value = _pluginConfigs[plugin.Author + "." + plugin.Name][key];
                }
            } catch { }

            return value;
        }

        private static Dictionary<string, string> LoadPluginConfig(MacroDeckPlugin plugin)
        {
            Dictionary<string, string> pluginConfig;
            if (!File.Exists(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json"))
            {
                pluginConfig = new Dictionary<string, string>();
            }
            else
            {
                pluginConfig = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json"), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                });
            }
            return pluginConfig;
        }


        public static void DeletePluginConfig(MacroDeckPlugin plugin)
        {
            if (_pluginConfigs.ContainsKey(plugin.Author + "." + plugin.Name))
            {
                _pluginConfigs.Remove(plugin.Author + "." + plugin.Name);
            }
            try
            {
                File.Delete(MacroDeck.PluginConfigPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json");
            }catch { }
        }

    }
}
