using System.Windows.Forms;
using CefSharp;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionStoreView : UserControl
{
    private readonly string extensionStoreUri = "https://macrodeck.org/extensionstore_plugins";


    public ExtensionStoreView()
    {
        InitializeComponent();

        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        webView.AddressChanged += WebView_AddressChanged;
        webView.LoadingStateChanged += WebView_LoadingStateChanged;
        webView.IsBrowserInitializedChanged += WebView_IsBrowserInitializedChanged;
            
        Load += ExtensionStoreView_Load;
    }

    private void WebView_IsBrowserInitializedChanged(object sender, EventArgs e)
    {
        if (webView.IsBrowserInitialized)
        {
            webView.LoadUrl(extensionStoreUri);
        }
    }


    private void WebView_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
    {
        Invoke(() =>
        {
            extensionStoreIcon.Image = e.IsLoading ? Resources.Spinner : Resources.Macro_Deck_2021;
        });
    }

    private void WebView_AddressChanged(object sender, AddressChangedEventArgs e)
    {
        var location = webView.Address;
        MacroDeckLogger.Trace(GetType(), $"Navigated to {location}");
        CheckHash();
        SetInstalledPlugins();
        SetInstalledIconPacks();
        Invoke(() =>
        {
            webView.Update();
        });
    }

    private void ExtensionStoreView_Load(object sender, EventArgs e)
    {
    }
    private void CheckHash()
    {
        var location = webView.Address;
        if (location.IndexOf("#") == -1 || location.IndexOf("=") == -1) return;
        var hash = location.Substring(location.IndexOf("#") + 1);
        var method = hash.Substring(0, hash.IndexOf("="));
        var packageId = hash.Substring(hash.IndexOf("=") + 1);
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
        var location = webView.Address;
        if (!location.Contains("extensionstore_iconpacks")) return;
        if (location.IndexOf("#") != -1)
        {
            location = location.Substring(0, location.IndexOf("#"));
        }
        if (location.IndexOf("?") == -1)
        {
            webView.LoadUrl($"{ location }?installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
            return;
        }
        if (!location.Contains("installed_iconpacks", StringComparison.OrdinalIgnoreCase))
        {
            webView.LoadUrl($"{ location }&installed_iconpacks={ ExtensionStoreHelper.InstalledIconPacksAsString }");
        }
    }

    private void SetInstalledPlugins()
    {
        var location = webView.Address;
        if (!location.Contains("extensionstore_plugins")) return;
        if (location.IndexOf("#") != -1)
        {
            location = location.Substring(0, location.IndexOf("#"));
        }
        if (location.IndexOf("?") == -1)
        {
            webView.LoadUrl($"{ location }?installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
            return;
        }
        if (!location.Contains("installed_plugins", StringComparison.OrdinalIgnoreCase))
        {
            webView.LoadUrl($"{ location }&installed_plugins={ ExtensionStoreHelper.InstalledPluginsAsString }");
        }
    }
}