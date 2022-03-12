using Microsoft.Web.WebView2.Core;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    public partial class ExtensionStoreView : UserControl
    {
        private readonly string extensionStoreUri = "https://macrodeck.org/extensionstore_plugins";

        public event EventHandler RequestClose;

        public ExtensionStoreView()
        {
            InitializeComponent();

            InitializeBrowser();
            this.webView.SourceChanged += WebView_SourceChanged;
            this.webView.NavigationStarting += WebView_NavigationStarting;
            this.webView.NavigationCompleted += WebView_NavigationCompleted;
            this.Load += ExtensionStoreView_Load;
        }

        private async void InitializeBrowser()
        {
            var env = await CoreWebView2Environment.CreateAsync(null, MacroDeck.UserDirectoryPath);
            await webView.EnsureCoreWebView2Async(env);
        }

        private void WebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            SpinnerDialog.SetVisisble(false, FindForm());
        }

        private void WebView_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            Task.Run(() => SpinnerDialog.SetVisisble(true, FindForm()));
        }

        private void ExtensionStoreView_Load(object sender, EventArgs e)
        {
            this.webView.Source = new Uri($"{ this.extensionStoreUri }");
        }

        private void WebView_SourceChanged(object sender, Microsoft.Web.WebView2.Core.CoreWebView2SourceChangedEventArgs e)
        {
            this.webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = Debugger.IsAttached;
            string location = this.webView.Source.OriginalString;
            MacroDeckLogger.Trace(GetType(), $"Navigated to {location}");
            CheckHash();
            SetInstalledPlugins();
            SetInstalledIconPacks();
        }

        private void CheckHash()
        {
            string location = this.webView.Source.OriginalString;
            if (location.IndexOf("#") == -1 || location.IndexOf("=") == -1) return;
            string hash = location.Substring(location.IndexOf("#") + 1);
            string method = hash.Substring(0, hash.IndexOf("="));
            string packageId = hash.Substring(hash.IndexOf("=") + 1);
            switch (method)
            {
                case "install":
                    MacroDeckLogger.Trace(GetType(), $"Installation requested for {packageId}");
                    ExtensionStoreHelper.InstallById(packageId);
                    break;
            }
        }

        private void SetInstalledIconPacks()
        {
            string location = this.webView.Source.OriginalString;
            if (!location.Contains("extensionstore_iconpacks")) return;
            if (location.IndexOf("#") != -1)
            {
                location = location.Substring(0, location.IndexOf("#"));
            }
            if (location.IndexOf("?") == -1)
            {
                this.webView.Source = new Uri($"{ location }?installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
                return;
            }
            if (!location.Contains("installed_iconpacks", StringComparison.OrdinalIgnoreCase))
            {
                this.webView.Source = new Uri($"{ location }&installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
                return;
            }
        }

        private void SetInstalledPlugins()
        {
            string location = this.webView.Source.OriginalString;
            if (!location.Contains("extensionstore_plugins")) return;
            if (location.IndexOf("#") != -1)
            {
                location = location.Substring(0, location.IndexOf("#"));
            }
            if (location.IndexOf("?") == -1)
            {
                this.webView.Source = new Uri($"{ location }?installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
                return;
            }
            if (!location.Contains("installed_plugins", StringComparison.OrdinalIgnoreCase))
            {
                this.webView.Source = new Uri($"{ location }&installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
                return;
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            if (RequestClose != null)
            {
                RequestClose(this, EventArgs.Empty);
            }
        }
    }
}
