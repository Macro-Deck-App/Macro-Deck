using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.Updater;

public class ProgressChangedEventArgs
{
    public int ProgressPercentage { get; set; }
}

public static class Updater
{
    public static bool UpdateAvailable;

    public static event EventHandler OnUpdateAvailable;
    public static event EventHandler OnLatestVersionInstalled;
    public static event EventHandler OnError;
    public static event EventHandler<ProgressChangedEventArgs> OnProgressChanged;

    private static bool _forceUpdate;
    private static bool _testChannel;

    private static JObject _jsonObject;
    public static JObject UpdateObject => _jsonObject;

    private static bool _downloading;
    public static bool Downloading => _downloading;

    private static int _progressPercentage;
    public static int ProgressPercentage => _progressPercentage;

    private static double _updateSizeMb;
    public static double UpdateSizeMb => _updateSizeMb;

    public static void Initialize(bool forceUpdate = false, bool testChannel = false)
    {
        _forceUpdate = forceUpdate;
        _testChannel = testChannel;
        if (MacroDeck.Configuration.AutoUpdates)
        {
            CheckForUpdatesAsync();
        }
        var updateCheckTimer = new Timer
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
        if (UpdateAvailable) return;
        try
        {
            using var wc = new WebClient();
            var jsonString = "";

            if (_testChannel)
            {
                jsonString = wc.DownloadString("https://macrodeck.org/files/versions.php?latest&channel=test");
            }

            jsonString = wc.DownloadString("https://macrodeck.org/files/versions.php?latest" + (MacroDeck.Configuration.UpdateBetaVersions ? "&beta" : ""));

            _jsonObject = JObject.Parse(jsonString);
            if (_jsonObject["build"] != null)
            {
                if (int.TryParse(_jsonObject["build"].ToString(), out var build))
                {
                    if (build > MacroDeck.Version.Build || _forceUpdate)
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
        } catch (Exception ex)
        {
            OnError?.Invoke(ex, EventArgs.Empty);
        }
    }

    private static double GetFileSizeMb(Uri uriPath)
    {
        var webRequest = HttpWebRequest.Create(uriPath);
        webRequest.Method = "HEAD";

        using var webResponse = webRequest.GetResponse();
        var fileSize = webResponse.Headers.Get("Content-Length");
        var fileSizeInMegaByte = Math.Round(Convert.ToDouble(fileSize) / 1024.0 / 1024.0, 2);
        return fileSizeInMegaByte;
    }

    public static void DownloadUpdate()
    {
        if (MacroDeck.StartParameters.PortableMode)
        {
            return;
        }
        _downloading = true;
        using var webClient = new WebClient();
        webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
        webClient.DownloadFileCompleted += WebClient_DownloadComplete;
        webClient.DownloadFileAsync(new Uri("https://macrodeck.org/files/installer/" + _jsonObject["filename"]), Path.Combine(MacroDeck.ApplicationPaths.TempDirectoryPath, _jsonObject["filename"].ToString()));
    }


    private static void WebClient_DownloadComplete(object sender, AsyncCompletedEventArgs e)
    {
        _downloading = false;
        try
        {
            if (!File.Exists(Path.Combine(MacroDeck.ApplicationPaths.TempDirectoryPath, _jsonObject["filename"].ToString())))
            {
                using (var msgBox = new MessageBox())
                {
                    msgBox.ShowDialog(LanguageManager.Strings.Error, LanguageManager.Strings.FileNotFound, MessageBoxButtons.OK);
                }

                OnError?.Invoke(null, EventArgs.Empty);
                return;
            }

            using (var stream = File.OpenRead(Path.Combine(MacroDeck.ApplicationPaths.TempDirectoryPath, _jsonObject["filename"].ToString())))
            {
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(stream);
                    var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (!checksumString.Equals(_jsonObject["md5"].ToString()))
                    {
                        using (var msgBox = new MessageBox())
                        {
                            msgBox.ShowDialog(LanguageManager.Strings.Error, LanguageManager.Strings.MD5NotValid, MessageBoxButtons.OK);
                        }

                        OnError?.Invoke(null, EventArgs.Empty);
                        return;
                    }
                }
            }

            var p = new Process
            {
                StartInfo = new ProcessStartInfo(Path.Combine(MacroDeck.ApplicationPaths.TempDirectoryPath, _jsonObject["filename"].ToString()))
                {
                    UseShellExecute = true
                }
            };
            p.Start();
            Environment.Exit(0);
        }
        catch
        {
            OnError?.Invoke(null, EventArgs.Empty);
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.Error, LanguageManager.Strings.TryAgainOrDownloadManually, MessageBoxButtons.OK);
        }
    }

    public static void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
    {
        _progressPercentage = e.ProgressPercentage;
        OnProgressChanged?.Invoke(sender, new ProgressChangedEventArgs { ProgressPercentage = e.ProgressPercentage });
    }


}