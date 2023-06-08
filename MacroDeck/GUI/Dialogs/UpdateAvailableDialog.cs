using SuchByte.MacroDeck.DataTypes.FileDownloader;
using SuchByte.MacroDeck.DataTypes.Updater;
using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Services;
using System.Diagnostics;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class UpdateAvailableDialog : DialogForm
{
    private readonly UpdateApiVersionInfo _availableUpdate;
    private readonly UpdateApiVersionFileInfo? _updateApiVersionFileInfo;

    public UpdateAvailableDialog(UpdateApiVersionInfo availableUpdate)
    {
        InitializeComponent();
        _availableUpdate = availableUpdate;
        _updateApiVersionFileInfo = _availableUpdate.Platforms?[UpdateService.PlatformIdentifier];
    }

    private void UpdateAvailableDialog_Load(object sender, EventArgs e)
    {
        lblVersion.Text = string.Format(LanguageManager.Strings.VersionXIsNowAvailable,
            _availableUpdate.Version,
            _availableUpdate.IsBeta == true ? "Beta" : "Release");
        btnInstall.Text = LanguageManager.Strings.DownloadAndInstall;
        lblSize.Text = $"Download size: {_updateApiVersionFileInfo?.FileSize.ConvertBytesToMegabytes().ToString("0.##")}MB";
        lblInstalledVersion.Text = $"Installed version: {MacroDeck.Version}";
    }

    private async void BtnInstall_Click(object sender, EventArgs e)
    {
        SetCloseIconVisible(false);
        btnInstall.Enabled = false;
        try
        {
            await UpdateService.Instance().DownloadAndInstallVersion(
                _availableUpdate,
                new Progress<DownloadProgressInfo>(UpdateProgress));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error($"Failed to download and install update\n{ex}");

            using var msgBox = new CustomControls.MessageBox();
            msgBox.ShowDialog(
                LanguageManager.Strings.Error,
                "An error occured during the update. Check the logs for more information.",
                MessageBoxButtons.OK);
        }
        finally
        {
            Close();
        }
    }

    private void UpdateProgress(DownloadProgressInfo downloadProgressInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProgress(downloadProgressInfo));
            return;
        }

        btnInstall.Progress = downloadProgressInfo.Percentage;
        btnInstall.Text = $"{downloadProgressInfo.Percentage}% - " +
                            $"{downloadProgressInfo.DownloadedBytes.ConvertBytesToMegabytes().ToString("0.##")}MB /" +
                            $"{downloadProgressInfo.TotalBytes.ConvertBytesToMegabytes().ToString("0.##")}MB " +
                            $" @{downloadProgressInfo.DownloadSpeed.ConvertBytesToMegabytes().ToString("0.##")}MB/s";
    }

    private void LblShowChangeNotes_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        var changeNotesUrl = _availableUpdate?.ChangeNotesUrl;
        if (changeNotesUrl == null)
        {
            return;
        }

        new Process
        {
            StartInfo = new ProcessStartInfo(changeNotesUrl)
            {
                UseShellExecute = true
            }
        }.Start();
    }
}
