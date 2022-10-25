using System.Diagnostics;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace SuchByte.MacroDeck.Configuration
{
    public class Configuration
    {
        bool _autoStart = true;

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
                        key.SetValue("Macro Deck", Process.GetCurrentProcess().MainModule.FileName);
                    }
                    else
                    {
                        var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                        key.DeleteValue("Macro Deck", false);
                    }
                }
                catch { }
            }
        }
        [JsonProperty("Icons.Cache")]
        public bool CacheIcons { get; set; } = true;

        [JsonProperty("Update.Auto")]
        public bool AutoUpdates { get; set; } = true;

        [JsonProperty("Update.InstallBeta")]
        public bool UpdateBetaVersions { get; set; }

        [JsonProperty("Connection.Host.Address")]
        public string Host_Address { get; set; }

        [JsonProperty("Connection.Host.Port")]
        public int Host_Port { get; set; } = 8191;

        [JsonProperty("Connection.AskOnNewConnections")]
        public bool AskOnNewConnections { get; set; } = true;

        [JsonProperty("Connection.BlockNewConnections")]
        public bool BlockNewConnections { get; set; }
        [JsonProperty("Language")]
        public string Language { get; set; } = "English";
    }
}
