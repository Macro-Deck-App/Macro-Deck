using System.IO;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Plugins;

public class PluginCredentials
{
    private static string FileName(MacroDeckPlugin plugin)
    {
        return $"{plugin.Author.ToLower()}_{plugin.Name.ToLower()}";
    }

    private static string FilePath(MacroDeckPlugin plugin)
    {
        return Path.Combine(ApplicationPaths.PluginCredentialsPath, FileName(plugin));
    }

    private static void Save(MacroDeckPlugin plugin, List<Dictionary<string, string>> pluginCredentials)
    {
        var serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
        };

        try
        {
            using var sw = new StreamWriter(FilePath(plugin));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, pluginCredentials);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(PluginCredentials), "Error while adding plugin credential: " + ex.Message);
        }
    }

    public static void AddCredentials(MacroDeckPlugin plugin, Dictionary<string, string> keyValuePairs)
    {
        var keyValuePairsEncrypted = new Dictionary<string, string>();

        foreach (var entry in keyValuePairs)
        {
            keyValuePairsEncrypted[entry.Key] = StringCipher.Encrypt(entry.Value, StringCipher.GetMachineGuid());
        }

        var pluginCredentials = GetEncryptedCredentials(plugin);

        pluginCredentials.Add(keyValuePairsEncrypted);

        Save(plugin, pluginCredentials);
    }

    public static void SetCredentials(MacroDeckPlugin plugin, Dictionary<string, string> keyValuePairs)
    {
        var keyValuePairsEncrypted = new Dictionary<string, string>();

        foreach (var entry in keyValuePairs)
        {
            keyValuePairsEncrypted[entry.Key] = StringCipher.Encrypt(entry.Value, StringCipher.GetMachineGuid());
        }

        List<Dictionary<string, string>> pluginCredentials = new();
        pluginCredentials.Add(keyValuePairsEncrypted);
        
        Save(plugin, pluginCredentials);
    }

    public static void DeletePluginCredentials(MacroDeckPlugin plugin)
    {
        File.Delete(FilePath(plugin));
    }

    public static List<Dictionary<string, string>> GetPluginCredentials(MacroDeckPlugin plugin)
    {
        var pluginCredentialsEncrypted = GetEncryptedCredentials(plugin);

        var pluginCredentialsDecrypted = new List<Dictionary<string, string>>();

        if (pluginCredentialsEncrypted == null)
        {
            return pluginCredentialsDecrypted;
        }
        
        foreach (var pluginCredentialEncrypted in pluginCredentialsEncrypted)
        {
            var pluginCredentialDecrypted = new Dictionary<string, string>();
            foreach (var entry in pluginCredentialEncrypted)
            {
                try
                {
                    pluginCredentialDecrypted[entry.Key] =
                        StringCipher.Decrypt(entry.Value, StringCipher.GetMachineGuid());
                }
                catch
                {
                    MacroDeckLogger.Warning(typeof(PluginCredentials),
                        $"Unable to decrypt credentials for {plugin.Name}. Perhaps the machine GUID changed?");
                }
            }

            pluginCredentialsDecrypted.Add(pluginCredentialDecrypted);
        }

        return pluginCredentialsDecrypted;
    }

    private static List<Dictionary<string, string>>? GetEncryptedCredentials(MacroDeckPlugin plugin)
    {
        if (!File.Exists(FilePath(plugin)))
        {
            return new();
        }
        
        return  JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(
            File.ReadAllText(FilePath(plugin)), 
            new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                });
    }
}