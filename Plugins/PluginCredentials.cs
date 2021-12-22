using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuchByte.MacroDeck.Plugins
{
    public class PluginCredentials
    {

        public static void AddCredentials(MacroDeckPlugin plugin, Dictionary<string, string> keyValuePairs)
        {
            Dictionary<string, string> keyValuePairsEncrypted = new Dictionary<string, string>();

            foreach (KeyValuePair<string, string> entry in keyValuePairs)
            {
                keyValuePairsEncrypted[entry.Key] = Utils.StringCipher.Encrypt(entry.Value, Utils.StringCipher.GetMachineGuid());
            }

            List<Dictionary<string, string>> pluginCredentials;

            if (!File.Exists(MacroDeck.PluginCredentialsPath + plugin.Author.ToLower() + "_" + plugin.Name.ToLower()))
            {
                pluginCredentials = new List<Dictionary<string, string>>();
            } else
            {
                pluginCredentials = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(File.ReadAllText(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                });
            }

            pluginCredentials.Add(keyValuePairsEncrypted);

            JsonSerializer serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            try
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, pluginCredentials);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        public static void SetCredentials(MacroDeckPlugin plugin, Dictionary<string, string> keyValuePairs)
        {
            Dictionary<string, string> keyValuePairsEncrypted = new Dictionary<string, string>();

            foreach (KeyValuePair<string, string> entry in keyValuePairs)
            {
                keyValuePairsEncrypted[entry.Key] = Utils.StringCipher.Encrypt(entry.Value, Utils.StringCipher.GetMachineGuid());
            }

            List<Dictionary<string, string>> pluginCredentials;

            pluginCredentials = new List<Dictionary<string, string>>();
            pluginCredentials.Add(keyValuePairsEncrypted);

            JsonSerializer serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            try
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, pluginCredentials);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public static void DeletePluginCredentials(MacroDeckPlugin plugin)
        {
            File.Delete(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower()));
        }

        public static List<Dictionary<string, string>> GetPluginCredentials(MacroDeckPlugin plugin)
        {
            List<Dictionary<string, string>> pluginCredentialsEncrypted;
            if (!File.Exists(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())))
            {
                pluginCredentialsEncrypted = new List<Dictionary<string, string>>();
            }
            else
            {
                pluginCredentialsEncrypted = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(File.ReadAllText(Path.Combine(MacroDeck.PluginCredentialsPath, plugin.Author.ToLower() + "_" + plugin.Name.ToLower())), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                });
            }

            List<Dictionary<string, string>> pluginCredentialsDecrypted = new List<Dictionary<string, string>>();

            foreach (Dictionary<string, string> pluginCredentialEncrypted in pluginCredentialsEncrypted)
            {
                Dictionary<string, string> pluginCredentialDecrypted = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> entry in pluginCredentialEncrypted)
                {
                    pluginCredentialDecrypted[entry.Key] = Utils.StringCipher.Decrypt(entry.Value, Utils.StringCipher.GetMachineGuid());
                }
                pluginCredentialsDecrypted.Add(pluginCredentialDecrypted);
            }

            return pluginCredentialsDecrypted;
        }


    }


}
