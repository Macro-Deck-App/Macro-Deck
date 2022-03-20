using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Updater
{
    public class ProgressChangedEventArgs
    {
        public int ProgressPercentage { get; set; }
    }

    public static class Updater
    {
        public static bool UpdateAvailable = false;

        public static event EventHandler OnUpdateAvailable;
        public static event EventHandler OnLatestVersionInstalled;
        public static event EventHandler OnError;
        public static event EventHandler<ProgressChangedEventArgs> OnProgressChanged;

        private static bool _forceUpdate = false;
        private static bool _testChannel = false;

        private static JObject _jsonObject;
        public static JObject UpdateObject { get { return _jsonObject; } }

        private static bool _downloading = false;
        public static bool Downloading { get { return _downloading; } }

        private static int _progressPercentage = 0;
        public static int ProgressPercentage { get { return _progressPercentage; } }

        private static double _updateSizeMb = 0;
        public static double UpdateSizeMb { get { return _updateSizeMb; } }

        public static void Initialize(bool forceUpdate = false, bool testChannel = false)
        {
            _forceUpdate = forceUpdate;
            _testChannel = testChannel;
            if (MacroDeck.Configuration.AutoUpdates)
            {
                CheckForUpdatesAsync();
            }
            Timer updateCheckTimer = new Timer
            {
                Enabled = true,
                Interval = 1000 * 60 * 10 // Check every 10 minutes
            };
        }

        private static void UpdateCheckTimerTick(object sender, EventArgs e)
        {
            if (MacroDeck.Configuration.AutoUpdates)
            {
                CheckForUpdatesAsync();
            }
        }

        public static void CheckForUpdatesAsync()
        {
            Task.Run(() =>
            {
                CheckForUpdates();
            });
        }

        private static void CheckForUpdates()
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    string jsonString = "";

                    if (_testChannel)
                    {
                        jsonString = wc.DownloadString("https://macrodeck.org/files/versions.php?latest&channel=test");
                    }

                    jsonString = wc.DownloadString("https://macrodeck.org/files/versions.php?latest" + (MacroDeck.Configuration.UpdateDevVersions ? "&dev" : "") + (MacroDeck.Configuration.UpdateBetaVersions ? "&beta" : ""));

                    _jsonObject = JObject.Parse(jsonString);
                    if (_jsonObject["build"] != null)
                    {
                        if (Int32.TryParse(_jsonObject["build"].ToString(), out int build))
                        {
                            if (build > MacroDeck.BuildVersion || _forceUpdate)
                            {
                                MacroDeckLogger.Info("Macro Deck version " + _jsonObject["version"] + " available");
                                try
                                {
                                    _updateSizeMb = GetFileSizeMb(new Uri("https://macrodeck.org/files/installer/" + _jsonObject["filename"]));
                                }
                                catch { }
                                if (OnUpdateAvailable != null)
                                {
                                    UpdateAvailable = true;
                                    OnUpdateAvailable(_jsonObject, EventArgs.Empty);
                                }
                            }
                            else
                            {
                                if (OnLatestVersionInstalled != null)
                                {
                                    UpdateAvailable = false;
                                    OnLatestVersionInstalled(_jsonObject, EventArgs.Empty);
                                }
                            }
                        }
                    }
                    
                }
            } catch (Exception ex)
            {
                if (OnError != null)
                {
                    OnError(ex, EventArgs.Empty);
                }
            }
        }

        private static double GetFileSizeMb(Uri uriPath)
        {
            var webRequest = HttpWebRequest.Create(uriPath);
            webRequest.Method = "HEAD";

            using (var webResponse = webRequest.GetResponse())
            {
                var fileSize = webResponse.Headers.Get("Content-Length");
                var fileSizeInMegaByte = Math.Round(Convert.ToDouble(fileSize) / 1024.0 / 1024.0, 2);
                return fileSizeInMegaByte;
            }
        }

        public static void DownloadUpdate()
        {
            if (MacroDeck.PortableMode)
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo("https://macrodeck.org/files/portable/" + _jsonObject["filename"])
                    {
                        UseShellExecute = true
                    }
                };
                p.Start();
                return;
            }
            _downloading = true;
            using (var webClient = new WebClient())
            {
                webClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(WebClient_DownloadProgressChanged);
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(WebClient_DownloadComplete);
                webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/installer/" + _jsonObject["filename"]), Path.Combine(MacroDeck.TempDirectoryPath, _jsonObject["filename"].ToString()));
            }
        }


        private static void WebClient_DownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            _downloading = false;
            try
            {
                if (!File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, _jsonObject["filename"].ToString())))
                {
                    using (var msgBox = new GUI.CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.FileNotFound, MessageBoxButtons.OK);
                    }
                    if (OnError != null)
                    {
                        OnError(null, EventArgs.Empty);
                    }
                    return;
                }

                using (var stream = File.OpenRead(Path.Combine(MacroDeck.TempDirectoryPath, _jsonObject["filename"].ToString())))
                {
                    using (var md5 = MD5.Create())
                    {
                        var hash = md5.ComputeHash(stream);
                        var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        if (!checksumString.Equals(_jsonObject["md5"].ToString()))
                        {
                            using (var msgBox = new GUI.CustomControls.MessageBox())
                            {
                                msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.MD5NotValid, MessageBoxButtons.OK);
                            }
                            if (OnError != null)
                            {
                                OnError(null, EventArgs.Empty);
                            }
                            return;
                        }
                    }
                }

                var p = new Process
                {
                    StartInfo = new ProcessStartInfo(Path.Combine(MacroDeck.TempDirectoryPath, _jsonObject["filename"].ToString()))
                    {
                        UseShellExecute = true
                    }
                };
                p.Start();
                Environment.Exit(0);
            }
            catch
            {
                if (OnError != null)
                {
                    OnError(null, EventArgs.Empty);
                }
                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.Error, Language.LanguageManager.Strings.TryAgainOrDownloadManually, MessageBoxButtons.OK);
                }
            }
        }

        public static void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            _progressPercentage = e.ProgressPercentage;
            if (OnProgressChanged != null)
            {
                OnProgressChanged(sender, new ProgressChangedEventArgs { ProgressPercentage = e.ProgressPercentage });
            }
        }


    }
}
