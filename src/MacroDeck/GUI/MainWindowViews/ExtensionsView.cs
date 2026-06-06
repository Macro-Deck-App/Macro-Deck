using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.MainWindowViews;

public partial class ExtensionsView : UserControl
{
    private ExtensionStoreView extensionStoreView;
    private ExtensionZipInstallerView extensionZipInstallerView;
    private InstalledExtensionsView installedExtensionsView;

    public ExtensionsView()
    {
        InitializeComponent();
        Dock = DockStyle.Fill;
        Name = LanguageManager.Strings.Extensions;
        radioInstalled.Text = LanguageManager.Strings.Installed;
        radioOnline.Text = LanguageManager.Strings.Online;
    }


    private void SetExtensionStoreView()
    {
        if (extensionStoreView == null || extensionStoreView.IsDisposed)
        {
            extensionStoreView = new ExtensionStoreView();
        }
        content.Controls.Clear();
        content.Controls.Add(extensionStoreView);
    }

    private void SetExtensionZipInstallerView()
    {
        if (extensionZipInstallerView == null || extensionZipInstallerView.IsDisposed)
        {
            extensionZipInstallerView = new ExtensionZipInstallerView();
            extensionZipInstallerView.RequestClose += ExtensionStoreView_RequestClose;
        }
        extensionZipInstallerView.Height = ClientRectangle.Height;
        extensionZipInstallerView.Width = ClientRectangle.Width;
        content.Controls.Clear();
        content.Controls.Add(extensionZipInstallerView);
    }

    private void ExtensionStoreView_RequestClose(object sender, EventArgs e)
    {
        SetInstalledExtensionsView();
    }

    private void SetInstalledExtensionsView()
    {
        if (installedExtensionsView == null)
        {
            installedExtensionsView = new InstalledExtensionsView();
            installedExtensionsView.RequestExtensionStore += InstalledExtensionsView_RequestExtensionStore;
            installedExtensionsView.RequestZipInstaller += InstalledExtensionsView_RequestZipInstaller;
        }

        extensionStoreView?.Dispose();
        content.Controls.Clear();
        content.Controls.Add(installedExtensionsView);
        installedExtensionsView.ListInstalledExtensions();
    }

    private void InstalledExtensionsView_RequestZipInstaller(object sender, EventArgs e)
    {
        SetExtensionZipInstallerView();
    }

    private void InstalledExtensionsView_RequestExtensionStore(object sender, EventArgs e)
    {
        SetExtensionStoreView();
    }

    private void ExtensionStoreView_Load(object sender, EventArgs e)
    {
        SetInstalledExtensionsView();
    }

    private void RadioInstalled_CheckedChanged(object sender, EventArgs e)
    {
        if (radioInstalled.Checked)
        {
            SetInstalledExtensionsView();
        }
    }

    private void RadioOnline_CheckedChanged(object sender, EventArgs e)
    {
        if (radioOnline.Checked)
        {
            SetExtensionStoreView();
        }
    }
}