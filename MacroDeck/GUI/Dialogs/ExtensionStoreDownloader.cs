using System.Runtime.InteropServices;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Plugins;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class ExtensionStoreDownloader : DialogForm
{
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
            PUtils.SetNativeEnabled(Owner.Handle, true);
    }

    public void DownloadAndInstall()
    {
        _pluginsToInstall = _packageIds.Count;
            
        Invoke(new Action(() => lblPackagesToDownload.Text =string.Format(LanguageManager.Strings.DownloadingAndInstallingXPackages, _pluginsToInstall)));
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
                        lblPackagesToDownload.Text = string.Format(LanguageManager.Strings.InstallationOfXPackagesDone, _pluginsToInstall);
                        btnDone.Visible = true;
                    });
                    MacroDeckLogger.Info(typeof(ExtensionStoreDownloader), $"*** Installation of {_pluginsToInstall} package(s) done ***");
                       
                }
            };
            Invoke(() => downloadList.Controls.Add(extensionStoreDownloaderItem));
        }
            
    }

    private void ExtensionStoreDownloader_Load(object sender, EventArgs e)
    {
        CenterToScreen();
        PUtils.SetNativeEnabled(Owner.Handle, false);
        Task.Run(() =>
        {
            DownloadAndInstall();
        });
    }

    private void BtnDone_Click(object sender, EventArgs e)
    {
        if (PluginManager.UpdatedPlugins.Count > 0)
        {
            using var msgBox = new MessageBox();
            if (msgBox.ShowDialog(LanguageManager.Strings.MacroDeckNeedsARestart, LanguageManager.Strings.MacroDeckMustBeRestartedForTheChanges, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                MacroDeck.RestartMacroDeck();
            }
        }
        Close();
    }
}

class PUtils
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