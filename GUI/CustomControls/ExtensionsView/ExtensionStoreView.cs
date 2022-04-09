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


        public ExtensionStoreView()
        {
            InitializeComponent();

            this.Dock = DockStyle.Fill;
            this.DoubleBuffered = true;

            this.webView.AddressChanged += WebView_AddressChanged;
            this.webView.LoadingStateChanged += WebView_LoadingStateChanged;
            this.webView.IsBrowserInitializedChanged += WebView_IsBrowserInitializedChanged;
            
            this.Load += ExtensionStoreView_Load;
        }

        private void WebView_IsBrowserInitializedChanged(object sender, EventArgs e)
        {
            if (this.webView.IsBrowserInitialized)
            {
                this.webView.LoadUrl(this.extensionStoreUri);
            }
        }


        private void WebView_LoadingStateChanged(object sender, CefSharp.LoadingStateChangedEventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                this.extensionStoreIcon.Image = e.IsLoading ? Properties.Resources.Spinner : Properties.Resources.Macro_Deck_2021;
            }));
        }

        private void WebView_AddressChanged(object sender, CefSharp.AddressChangedEventArgs e)
        {
            string location = this.webView.Address;
            MacroDeckLogger.Trace(GetType(), $"Navigated to {location}");
            CheckHash();
            SetInstalledPlugins();
            SetInstalledIconPacks();
            this.Invoke(new Action(() =>
            {
                this.webView.Update();
            }));
        }

        private void ExtensionStoreView_Load(object sender, EventArgs e)
        {
        }
        private void CheckHash()
        {
            string location = this.webView.Address;
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
            string location = this.webView.Address;
            if (!location.Contains("extensionstore_iconpacks")) return;
            if (location.IndexOf("#") != -1)
            {
                location = location.Substring(0, location.IndexOf("#"));
            }
            if (location.IndexOf("?") == -1)
            {
                this.webView.LoadUrl($"{ location }?installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
                return;
            }
            if (!location.Contains("installed_iconpacks", StringComparison.OrdinalIgnoreCase))
            {
                this.webView.LoadUrl($"{ location }&installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
                return;
            }
        }

        private void SetInstalledPlugins()
        {
            string location = this.webView.Address;
            if (!location.Contains("extensionstore_plugins")) return;
            if (location.IndexOf("#") != -1)
            {
                location = location.Substring(0, location.IndexOf("#"));
            }
            if (location.IndexOf("?") == -1)
            {
                this.webView.LoadUrl($"{ location }?installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
                return;
            }
            if (!location.Contains("installed_plugins", StringComparison.OrdinalIgnoreCase))
            {
                this.webView.LoadUrl($"{ location }&installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
                return;
            }
        }
    }
}
