using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader
{
    public partial class ExtensionStoreDownloaderItem : RoundedUserControl
    {
        public ExtensionStoreDownloaderPackageInfoModel PackageInfo { get; private set; }

        public ExtensionStoreExtensionModel ExtensionModel { get; private set; }

        public event EventHandler OnInstallationCompleted;


        private WebClient _webClient;

        private bool _cancel;

        public bool Cancel
        {
            get => _cancel;
            private set
            {
                _cancel = true;
                _webClient?.CancelAsync();
                Invoke(() =>
                {
                    lblStatus.Text = LanguageManager.Strings.Cancelled;
                    progressBar.Visible = false;
                    btnAbort.Visible = false;
                });

                OnInstallationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public ExtensionStoreDownloaderItem(ExtensionStoreDownloaderPackageInfoModel extensionStoreDownloaderPackageInfoModel)
        {
            InitializeComponent();
            PackageInfo = extensionStoreDownloaderPackageInfoModel;
        }

        private void ExtensionStoreDownloaderItem_Load(object sender, EventArgs e)
        {
            try
            {
                DownloadAndInstallPackage();
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(GetType(), ex.Message);
                Invoke(() =>
                {
                    lblStatus.Text = LanguageManager.Strings.Error;
                });
            }
        }

        private void DownloadAndInstallPackage()
        {
            Invoke(() =>
            {
                lblStatus.Text = LanguageManager.Strings.Preparing;
            });
            using (_webClient = new WebClient())
            {
                ExtensionModel = JsonConvert.DeserializeObject<ExtensionStoreExtensionModel>(_webClient.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=info&package-id={PackageInfo.PackageId}"), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => {
                        MacroDeckLogger.Error(typeof(ExtensionStoreDownloaderItem), "Error while deserializing the ExtensionStoreModel file: " + args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                });
                _webClient.DownloadProgressChanged += Wc_DownloadProgressChanged;
                _webClient.DownloadFileCompleted += Wc_DownloadFileCompleted;
                var url = $"https://macrodeck.org/files/extensionstore/{ExtensionModel.PackageId}/{ExtensionModel.Filename}";

                var iconBitmap = Resources.Macro_Deck_2021_update;
                try
                {
                    iconBitmap = (Bitmap)Base64.GetImageFromBase64(ExtensionModel.IconBase64);
                } catch { }

                Invoke(() =>
                {
                    extensionIcon.BackgroundImage = iconBitmap;
                    lblPackageName.Text = string.Format(LanguageManager.Strings.ExtensionStoreDownloaderPackageIdVersion, PackageInfo.PackageId, ExtensionModel.Version);
                });

                MacroDeckLogger.Trace(typeof(ExtensionStoreDownloaderItem), $"Download {url}");
                _webClient.DownloadFileAsync(new Uri(url), Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename));

            }
        }

        private void Wc_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                return;
            }
            Invoke(() =>
            {
                progressBar.Visible = false;
                lblStatus.Text = LanguageManager.Strings.Installing;
            });
            try
            {
                Install();
            }
            catch (Exception ex)
            {
            
            }
        }

        private void Wc_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (Cancel)
            {
                return;
            }
            Invoke(() =>
            {
                progressBar.Visible = true;
                lblStatus.Text = $"{LanguageManager.Strings.Downloading} {(e.BytesReceived / 1024f / 1024f).ToString("0.00")} MB / {(e.TotalBytesToReceive / 1024f / 1024f).ToString("0.00")} MB";
                progressBar.Progress = e.ProgressPercentage;
            });
        }

        private void Install()
        {
            if (!File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename)))
            {
                Finished();
                MacroDeckLogger.Error(GetType(), $"Download of {ExtensionModel.Name} failed: {ExtensionModel.Filename} not found");
                return;
            }

            using (var stream = File.OpenRead(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename)))
            {
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(stream);
                    var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (!checksumString.Equals(ExtensionModel.Md5Checksum))
                    {
                        Finished();
                        MacroDeckLogger.Error(GetType(), $"md5 checksum of {ExtensionModel.Name} not matching!" + Environment.NewLine +
                                                $"md5 checksum on server: {ExtensionModel.Md5Checksum}" + Environment.NewLine +
                                                $"md5 checksum of downloaded file: {checksumString}");
                        return;
                    }
                }
            }

            MacroDeckLogger.Info(GetType(), $"Start installation of {ExtensionModel.Name} version {ExtensionModel.Version}");


            ExtensionManifestModel extensionManifestModel;
            try
            {
                extensionManifestModel = ExtensionManifestModel.FromZipFilePath(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename));
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(GetType(), $"Error while deserializing ExtensionManifest for {ExtensionModel.Name}: " + ex.Message + Environment.NewLine + ex.StackTrace);
                Finished();
                return;
            }

            if (extensionManifestModel == null)
            {
                MacroDeckLogger.Error(GetType(), $"ExtensionManifest for {ExtensionModel.Name} not found. Installation aborted!");
                Finished();
                return;
            }

            if (Cancel)
            {
                return;
            }
            switch (extensionManifestModel.Type)
            {
                case ExtensionType.Plugin:
                    try
                    {
                        PluginManager.InstallPluginFromZip(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename));
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error(GetType(), $"Error while installing {ExtensionModel.Name}: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        Finished();
                        return;
                    }
                    break;
                case ExtensionType.IconPack:
                    try
                    {
                        IconManager.InstallIconPackZip(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename), true);
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error(GetType(), $"Error while installing {ExtensionModel.Name}: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        Finished();
                        return;
                    }
                    break;
            }

            try
            {
                if (Directory.Exists(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.PackageId)))
                {
                    Directory.Delete(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.PackageId), true);
                }
                if (File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename)))
                {
                    File.Delete(Path.Combine(MacroDeck.TempDirectoryPath, ExtensionModel.Filename));
                }
            }
            catch { }

            Finished();
        }

        private void Finished(bool error = false)
        {
            Invoke(() =>
            {
                btnAbort.Visible = false;
                lblStatus.Text = error ? LanguageManager.Strings.Error : LanguageManager.Strings.Completed;
            });
            OnInstallationCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void BtnAbort_Click(object sender, EventArgs e)
        {
            if (!Cancel)
            {
                Cancel = true;
            }
        }
    }
}
