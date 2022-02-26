using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;
using SuchByte.MacroDeck.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowViews
{
    public partial class ExtensionsView : UserControl
    {
        private ExtensionStoreView extensionStoreView;
        private ExtensionZipInstallerView extensionZipInstallerView;
        private InstalledExtensionsView installedExtensionsView;

        public ExtensionsView()
        {
            InitializeComponent();
            this.Name = "Extensions";
        }


        private void SetExtensionStoreView()
        {
            if (this.extensionStoreView == null || this.extensionStoreView.IsDisposed)
            {
                this.extensionStoreView = new ExtensionStoreView();
                this.extensionStoreView.RequestClose += ExtensionStoreView_RequestClose;
            }
            this.Controls.Clear();
            this.Controls.Add(this.extensionStoreView);
        }

        private void SetExtensionZipInstallerView()
        {
            if (this.extensionZipInstallerView == null || this.extensionZipInstallerView.IsDisposed)
            {
                this.extensionZipInstallerView = new ExtensionZipInstallerView();
                this.extensionZipInstallerView.RequestClose += ExtensionStoreView_RequestClose;
            }
            this.Controls.Clear();
            this.Controls.Add(this.extensionZipInstallerView);
        }

        private void ExtensionStoreView_RequestClose(object sender, EventArgs e)
        {
            this.SetInstalledExtensionsView();
        }

        private void SetInstalledExtensionsView()
        {
            if (this.installedExtensionsView == null)
            {
                this.installedExtensionsView = new InstalledExtensionsView();
                this.installedExtensionsView.RequestExtensionStore += InstalledExtensionsView_RequestExtensionStore;
                this.installedExtensionsView.RequestZipInstaller += InstalledExtensionsView_RequestZipInstaller;
            }
            if (this.extensionStoreView != null)
            {
                this.extensionStoreView.RequestClose -= this.ExtensionStoreView_RequestClose;
                this.extensionStoreView.Dispose();
            }
            this.Controls.Clear();
            this.Controls.Add(this.installedExtensionsView);
            this.installedExtensionsView.ListInstalledExtensions();
        }

        private void InstalledExtensionsView_RequestZipInstaller(object sender, EventArgs e)
        {
            this.SetExtensionZipInstallerView();
        }

        private void InstalledExtensionsView_RequestExtensionStore(object sender, EventArgs e)
        {
            this.SetExtensionStoreView();
        }

        private void ExtensionStoreView_Load(object sender, EventArgs e)
        {
            this.SetInstalledExtensionsView();
        }
    }
}
