using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class ExtensionStoreDownloader : DialogForm
    {
        private int _pluginsToInstall = 0;
        private int _pluginsInstalled = 0;

        private List<ExtensionStoreDownloaderPackageInfoModel> _packageIds;

        public ExtensionStoreDownloader(List<ExtensionStoreDownloaderPackageInfoModel> packageIds)
        {
            this.Owner = MacroDeck.MainWindow;
            this._packageIds = packageIds;
            InitializeComponent();
            this.SetCloseIconVisible(false);

            this.FormClosing += ExtensionStoreDownloader_FormClosing;
        }

        private void ExtensionStoreDownloader_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!e.Cancel)
                PUtils.SetNativeEnabled(this.Owner.Handle, true);
        }

        public void DownloadAndInstall()
        {
            this._pluginsToInstall = this._packageIds.Count;
            AppendText($"Starting download of {this._pluginsToInstall} package(s)...", Color.White);
            foreach(var packageInfo in this._packageIds)
            {
                /*using (WebClient wc = new WebClient())
                {
                    switch (packageInfo.ExtensionType)
                    {
                        case ExtensionType.Plugin:
                            wc.DownloadString($"https://macrodeck.org/files/packagemanager/plugins.php?action=countdl&uuid={packageInfo.PackageId}");
                            break;
                        case ExtensionType.IconPack:
                            wc.DownloadString($"https://macrodeck.org/files/packagemanager/iconpacks.php?action=countdl&uuid={packageInfo.PackageId}");
                            break;
                    }
                }*/

                using (WebClient wc = new WebClient())
                {
                    var extensionStoreModel = JsonConvert.DeserializeObject<ExtensionStoreExtensionModel>(wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=info&package-id={packageInfo.PackageId}"), new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) => {
                            MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), "Error while deserializing the ExtensionStoreModel file: " + args.ErrorContext.Error.Message);
                            args.ErrorContext.Handled = true;
                        },
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    });
                    wc.DownloadProgressChanged += Wc_DownloadProgressChanged;
                    wc.DownloadFileCompleted += DownloadComplete(extensionStoreModel);
                    string url = $"https://macrodeck.org/files/extensionstore/{extensionStoreModel.PackageId}/{extensionStoreModel.Filename}";

                    
                    
                    MacroDeckLogger.Trace(typeof(ExtensionStoreDownloader), $"Download {url}");
                    AppendText($"Starting download of ({packageInfo.PackageId}) {extensionStoreModel.Name} {extensionStoreModel.Version}...", Color.White);
                    wc.DownloadFileAsync(new Uri(url), Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename));

                }
                
            }
            
        }

        private AsyncCompletedEventHandler DownloadComplete(ExtensionStoreExtensionModel extensionStoreModel)
        {
            Action<object, AsyncCompletedEventArgs> action = (sender, e) =>
            {
                AppendText($"Download of {extensionStoreModel.Name} {extensionStoreModel.Version} complete", Color.White);
                if (e.Error != null)
                {
                    MacroDeckLogger.Error("Plugin download failed: " + e.Error.Message);
                }

                if (!File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename)))
                {
                    AppendText($"Download of {extensionStoreModel.Name} failed: {extensionStoreModel.Filename} not found", Color.Red);
                    MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), $"Download of {extensionStoreModel.Name} failed: {extensionStoreModel.Filename} not found");
                    return;
                }

                using (var stream = File.OpenRead(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename)))
                {
                    using (var md5 = MD5.Create())
                    {
                        var hash = md5.ComputeHash(stream);
                        var checksumString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        if (!checksumString.Equals(extensionStoreModel.Md5Checksum))
                        {
                            AppendText($"The md5 checksum of the downloaded file of {extensionStoreModel.Name} does not match the md5 checksum on the server. Installation aborted!", Color.Red);

                            MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), $"md5 checksum of {extensionStoreModel.Name} not matching!" + Environment.NewLine +
                                                    $"md5 checksum on server: {extensionStoreModel.Md5Checksum}" + Environment.NewLine +
                                                    $"md5 checksum of downloaded file: {checksumString}");
                            return;
                        }
                    }
                }

                AppendText($"Start installation of {extensionStoreModel.Name} version {extensionStoreModel.Version}", Color.White);

                MacroDeckLogger.Info(typeof(ExtensionStoreDownloader), $"Start installation of {extensionStoreModel.Name} version {extensionStoreModel.Version}");


                JsonSerializer serializer = new JsonSerializer();
                ExtensionManifestModel extensionManifestModel = null;
                try
                {
                    extensionManifestModel = ExtensionManifestModel.FromZipFilePath(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename));
                } catch (Exception ex)
                {
                    MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), $"Error while deserializing ExtensionManifest for {extensionStoreModel.Name}: " + ex.Message + Environment.NewLine + ex.StackTrace);
                    AppendText($"Start installation of {extensionStoreModel.Name} version {extensionStoreModel.Version} failed. Check logs for more information", Color.Red);
                    return;
                }

                if (extensionManifestModel == null)
                {
                    MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), $"ExtensionManifest for {extensionStoreModel.Name} not found. Installation aborted!");
                    AppendText($"ExtensionManifest for {extensionStoreModel.Name} not found. Installation aborted!", Color.Red);
                    return;
                }

                if (extensionManifestModel.Type == ExtensionType.Plugin)
                {
                    try
                    {
                        PluginManager.InstallPluginFromZip(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename));
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Error(typeof(ExtensionStoreDownloader), $"Error while installing {extensionStoreModel.Name}: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        AppendText($"Start installation of {extensionStoreModel.Name} version {extensionStoreModel.Version} failed. Check logs for more information", Color.Red);
                        return;
                    }

                    try
                    {
                        if (Directory.Exists(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.PackageId)))
                        {
                            Directory.Delete(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.PackageId), true);
                        }
                        if (File.Exists(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename)))
                        {
                            File.Delete(Path.Combine(MacroDeck.TempDirectoryPath, extensionStoreModel.Filename));
                        }
                    }
                    catch { }
                } else if (extensionManifestModel.Type == ExtensionType.IconPack)
                {
                    // TODO
                }
                


                AppendText($"Installation of {extensionStoreModel.Name} version {extensionStoreModel.Version} completed", Color.Lime);
                MacroDeckLogger.Info(typeof(ExtensionStoreDownloader), $"Installation of {extensionStoreModel.Name} version {extensionStoreModel.Version} completed");

                this._pluginsInstalled++;
                if (this._pluginsInstalled == this._pluginsToInstall)
                {
                    AppendText($"{Environment.NewLine} *** Installation of {this._pluginsToInstall} package(s) done ***{Environment.NewLine}", Color.Aqua);
                    MacroDeckLogger.Info(typeof(ExtensionStoreDownloader), $"*** Installation of {this._pluginsToInstall} package(s) done ***");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    this.btnDone.Visible = true;
                }

            };
            return new AsyncCompletedEventHandler(action);
        }

        private void Wc_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
        }

        public void AppendText(string text, Color color)
        {
            this.Invoke(new Action(() =>
            {
                this.output.SelectionStart = this.output.TextLength;
                this.output.SelectionLength = 0;

                this.output.SelectionColor = color;
                this.output.AppendText(text + Environment.NewLine);
                this.output.SelectionColor = this.output.ForeColor;
                this.output.ScrollToCaret();
            }));
        }

        private void ExtensionStoreDownloader_Load(object sender, EventArgs e)
        {
            CenterToScreen();
            PUtils.SetNativeEnabled(this.Owner.Handle, false);
            DownloadAndInstall();
        }

        private void BtnDone_Click(object sender, EventArgs e)
        {
            if (PluginManager.UpdatedPlugins.Count > 0)
            {
                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    if (msgBox.ShowDialog(Language.LanguageManager.Strings.MacroDeckNeedsARestart, Language.LanguageManager.Strings.MacroDeckMustBeRestartedForTheChanges, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        Process.Start(Path.Combine(MacroDeck.MainDirectoryPath, AppDomain.CurrentDomain.FriendlyName), "--show");
                        Environment.Exit(0);
                    }
                }
            }
            this.Close();
        }
    }

    partial class PUtils
    {
        const int GWL_STYLE = -16;
        const int WS_DISABLED = 0x08000000;


        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);


        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        static public void SetNativeEnabled(IntPtr hWnd, bool enabled)
        {
            SetWindowLong(hWnd, GWL_STYLE, GetWindowLong(hWnd, GWL_STYLE) & ~WS_DISABLED | (enabled ? 0 : WS_DISABLED));
        }
    }
}
