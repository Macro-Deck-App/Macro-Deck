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
        public static void SetValue(MacroDeckPlugin plugin, string key, string value)
        {
            try
            {
                Dictionary<string, string> pluginConfig;

                if (!File.Exists(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())))
                {
                    pluginConfig = new Dictionary<string, string>();
                }
                else
                {
                    pluginConfig = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json")), new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                    });
                }

                pluginConfig[key] = value;

                JsonSerializer serializer = new JsonSerializer
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                };

                using (StreamWriter sw = new StreamWriter(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json")))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, pluginConfig);
                }
            } catch { }
        }


        public static string GetValue(MacroDeckPlugin plugin = null, string key = "")
        {
            string value = "";
            try
            {
                if (plugin == null || key == null) return value;

                Dictionary<string, string> pluginConfig;
                if (!File.Exists(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json")))
                {
                    pluginConfig = new Dictionary<string, string>();
                }
                else
                {
                    pluginConfig = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json")), new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) => { args.ErrorContext.Handled = true; }
                    });
                }

                if (pluginConfig != null && !String.IsNullOrWhiteSpace(pluginConfig[key]))
                {
                    value = pluginConfig[key];
                }
            } catch {}

            return value;
        }

        public static void DeletePluginConfig(MacroDeckPlugin plugin)
        {
            try
            {
                File.Delete(Path.Combine(MacroDeck.PluginConfigPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower() + ".json"));
            }catch { }
        }

    }
}
