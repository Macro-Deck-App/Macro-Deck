using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader
{
    public partial class ExtensionStoreDownloaderItem : RoundedUserControl
    {
        public ExtensionStoreDownloaderPackageInfoModel PackageInfo { get; private set; }

        public ExtensionStoreExtensionModel ExtensionModel { get; private set; }

        public event EventHandler OnInstallationCompleted;


        private WebClient _webClient;

        private bool _cancel = false;

        public bool Cancel
        {
            get => _cancel;
            private set
            {
                _cancel = true;
                if (_webClient != null)
                {
                    _webClient.CancelAsync();
                }
                this.Invoke(new Action(() =>
                {
                    this.lblStatus.Text = $"Cancelled";
                    this.progressBar.Visible = false;
                    this.btnAbort.Visible = false;
                }));

                if (OnInstallationCompleted != null)
                {
                    OnInstallationCompleted(this, EventArgs.Empty);
                }
            }
        }

        public ExtensionStoreDownloaderItem(ExtensionStoreDownloaderPackageInfoModel extensionStoreDownloaderPackageInfoModel)
        {
            InitializeComponent();
            this.PackageInfo = extensionStoreDownloaderPackageInfoModel;
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
                this.Invoke(new Action(() =>
                {
                    this.lblStatus.Text = $"Error";
                }));
            }
        }

        private void DownloadAndInstallPackage()
        {
            this.Invoke(new Action(() =>
            {
                this.lblStatus.Text = $"Preparing...";
            }));
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
                string url = $"https://macrodeck.org/files/extensionstore/{ExtensionModel.PackageId}/{ExtensionModel.Filename}";

                Bitmap iconBitmap = Properties.Resources.Macro_Deck_2021_update;
                try
                {
                    iconBitmap = (Bitmap)Utils.Base64.GetImageFromBase64(ExtensionModel.IconBase64);
                } catch { }

                this.Invoke(new Action(() =>
                {
                    this.extensionIcon.BackgroundImage = iconBitmap;
                    this.lblPackageName.Text = $"{PackageInfo.PackageId} version {ExtensionModel.Version}";
                }));

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
            this.Invoke(new Action(() =>
            {
                this.progressBar.Visible = false;
                this.lblStatus.Text = $"Installing...";
            }));
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
            if (this.Cancel)
            {
                return;
            }
            this.Invoke(new Action(() =>
            {
                this.progressBar.Visible = true;
                this.lblStatus.Text = $"Downloading... {(e.BytesReceived / 1024f / 1024f).ToString("0.00")} MB / {(e.TotalBytesToReceive / 1024f / 1024f).ToString("0.00")} MB";
                this.progressBar.Progress = e.ProgressPercentage;
            }));
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


            JsonSerializer serializer = new JsonSerializer();
            ExtensionManifestModel extensionManifestModel = null;
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
            this.Invoke(new Action(() =>
            {
                this.btnAbort.Visible = false;
                this.lblStatus.Text = error ? $"Error" : "Completed";
            }));
            if (OnInstallationCompleted != null)
            {
                OnInstallationCompleted(this, EventArgs.Empty);
            }
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
