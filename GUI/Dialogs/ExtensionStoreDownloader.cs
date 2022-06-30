using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader;
using SuchByte.MacroDeck.Icons;
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
            
            this.Invoke(new Action(() => this.lblPackagesToDownload.Text = $"Downloading and installing {this._pluginsToInstall} package(s)"));
            foreach (var packageInfo in this._packageIds)
            {
                var extensionStoreDownloaderItem = new ExtensionStoreDownloaderItem(packageInfo);
                extensionStoreDownloaderItem.OnInstallationCompleted += (sender, e) =>
                {
                    this._pluginsInstalled++;
                    if (this._pluginsInstalled == this._pluginsToInstall)
                    {
                        this.Invoke(new Action(() =>
                        {
                            this.lblPackagesToDownload.Text = $"Installation of {this._pluginsToInstall} package(s) done";
                            this.btnDone.Visible = true;
                        }));
                        MacroDeckLogger.Info(typeof(ExtensionStoreDownloader), $"*** Installation of {this._pluginsToInstall} package(s) done ***");
                       
                    }
                };
                this.Invoke(new Action(() => this.downloadList.Controls.Add(extensionStoreDownloaderItem)));
            }
            
        }

        private void ExtensionStoreDownloader_Load(object sender, EventArgs e)
        {
            CenterToScreen();
            PUtils.SetNativeEnabled(this.Owner.Handle, false);
            Task.Run(() =>
            {
                DownloadAndInstall();
            });
        }

        private void BtnDone_Click(object sender, EventArgs e)
        {
            if (PluginManager.UpdatedPlugins.Count > 0)
            {
                using (var msgBox = new CustomControls.MessageBox())
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
