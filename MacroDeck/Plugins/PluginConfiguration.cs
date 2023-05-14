using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Startup;

namespace SuchByte.MacroDeck.Plugins;

public class PluginConfiguration
{
    private static string FileName(MacroDeckPlugin plugin)
    {
        return $"{plugin.Author.ToLower()}_{plugin.Name.ToLower()}.json";
    }

    private static string FilePath(MacroDeckPlugin plugin)
    {
        return Path.Combine(ApplicationPaths.PluginConfigPath, FileName(plugin));
    }
    
    public static void SetValue(MacroDeckPlugin plugin, string key, string value)
    {
        try
        {
            var pluginConfig = GetConfig(plugin);

            pluginConfig[key] = value;

            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            using var sw = new StreamWriter(FilePath(plugin));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, pluginConfig);
        } catch { }
    }

    private static Dictionary<string, string>? GetConfig(MacroDeckPlugin plugin)
    {
        if (!File.Exists(FilePath(plugin)))
        {
            return new Dictionary<string, string>();
        }
        
        return JsonConvert.DeserializeObject<Dictionary<string, string>>(
            File.ReadAllText(FilePath(plugin)),
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            });
    }


    public static string GetValue(MacroDeckPlugin plugin = null, string key = "")
    {
        var value = "";
        try
        {
            if (plugin == null || key == null) return value;

            Dictionary<string, string> pluginConfig = GetConfig(plugin);

            if (pluginConfig != null && !string.IsNullOrWhiteSpace(pluginConfig[key]))
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
            File.Delete(FilePath(plugin));
        }catch { }
    }

}