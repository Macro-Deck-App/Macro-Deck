using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Configuration;

public class MainConfiguration
{
    private bool _autoStart = true;
    [JsonProperty("AutoStart")]
    public bool AutoStart { get => _autoStart;
        set
        {
            _autoStart = value;
            try
            {
                if (value)
                {
                    var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    key?.SetValue("Macro Deck", Process.GetCurrentProcess().MainModule.FileName);
                }
                else
                {
                    var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    key?.DeleteValue("Macro Deck", false);
                }
            }
            catch
            {
                // ignored
            }
        }
    }
    [JsonProperty("Icons.Cache")]
    public bool CacheIcons { get; set; } = true;

    [JsonProperty("Update.Auto")]
    public bool AutoUpdates { get; set; } = true;

    [JsonProperty("Update.InstallBeta")]
    public bool UpdateBetaVersions { get; set; }

    [JsonProperty("Connection.Host.Address")]
    public string HostAddress { get; set; } = "127.0.0.1";

    [JsonProperty("Connection.Host.Port")]
    public int HostPort { get; set; } = 8191;

    [JsonProperty("Connection.AskOnNewConnections")]
    public bool AskOnNewConnections { get; set; } = true;

    [JsonProperty("Connection.BlockNewConnections")]
    public bool BlockNewConnections { get; set; }
    [JsonProperty("Language")]
    public string Language { get; set; } = "English";

    public void Save(string path)
    {
        var serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        try
        {
            using var sw = new StreamWriter(path);
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, this);

            MacroDeckLogger.Info("Configuration saved");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error("Failed to save configuration: " + ex.Message);
        }
    }

    public static MainConfiguration LoadFromFile(string path)
    {
        return JsonConvert.DeserializeObject<MainConfiguration>(File.ReadAllText(path)) ?? new MainConfiguration();
    }
}