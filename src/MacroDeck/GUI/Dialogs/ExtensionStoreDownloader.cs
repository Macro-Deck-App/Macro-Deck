using System.Runtime.InteropServices;
using SuchByte.MacroDeck.GUI.CustomControls;
using Serilog;
using SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class ExtensionStoreDownloader : DialogForm
{
    private static readonly ILogger logger = Log.ForContext(typeof(ExtensionStoreDownloader));

    private int _pluginsToInstall;
    private int _pluginsInstalled;

    private List<ExtensionStoreDownloaderPackageInfoModel> _packageIds;


    public ExtensionStoreDownloader(List<ExtensionStoreDownloaderPackageInfoModel> packageIds)
    {
        Owner = MacroDeck.MainWindow;
        _packageIds = packageIds;
        InitializeComponent();
        SetCloseIconVisible(false);

        FormClosing += ExtensionStoreDownloader_FormClosing;
    }

    private void ExtensionStoreDownloader_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!e.Cancel)
        {
            PUtils.SetNativeEnabled(Owner.Handle, true);
        }
    }

    public void DownloadAndInstall()
    {
        _pluginsToInstall = _packageIds.Count;

        Invoke(new Action(() =>
            lblPackagesToDownload.Text = string.Format(LanguageManager.Strings.DownloadingAndInstallingXPackages,
                _pluginsToInstall)));
        foreach (var packageInfo in _packageIds)
        {
            var extensionStoreDownloaderItem = new ExtensionStoreDownloaderItem(packageInfo);
            extensionStoreDownloaderItem.OnInstallationCompleted += (sender, e) =>
            {
                _pluginsInstalled++;
                if (_pluginsInstalled == _pluginsToInstall)
                {
                    Invoke(() =>
                    {
                        lblPackagesToDownload.Text = string.Format(LanguageManager.Strings.InstallationOfXPackagesDone,
                            _pluginsToInstall);
                        btnDone.Visible = true;
                    });
                    logger.Information(
                        $"*** Installation of {_pluginsToInstall} package(s) done ***");
                }
            };
            Invoke(() => downloadList.Controls.Add(extensionStoreDownloaderItem));
        }
    }

    private void ExtensionStoreDownloader_Load(object sender, EventArgs e)
    {
        CenterToScreen();
        PUtils.SetNativeEnabled(Owner.Handle, false);
        Task.Run(DownloadAndInstall);
    }

    private void BtnDone_Click(object sender, EventArgs e)
    {
        if (PluginManager.UpdatedPlugins.Count > 0)
        {
            MacroDeck.RequestRestart();
        }

        Close();
    }
}

internal class PUtils
{
    private const int GWL_STYLE = -16;
    private const int WS_DISABLED = 0x08000000;


    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);


    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


    public static void SetNativeEnabled(IntPtr hWnd, bool enabled)
    {
        SetWindowLong(hWnd, GWL_STYLE, (GetWindowLong(hWnd, GWL_STYLE) & ~WS_DISABLED) | (enabled ? 0 : WS_DISABLED));
    }
}
