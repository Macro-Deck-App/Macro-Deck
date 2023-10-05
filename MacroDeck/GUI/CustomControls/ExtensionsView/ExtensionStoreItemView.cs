using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Threading;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionStoreItemView : RoundedUserControl
{
    private readonly ApiV2ExtensionSummary _extension;
    private bool _installed = false;

    public ExtensionStoreItemView(ApiV2ExtensionSummary extension)
    {
        InitializeComponent();
        _extension = extension;
        lblExtensionName.Text = _extension.Name;
        lblDescription.Text = !string.IsNullOrWhiteSpace(_extension.Description) ? _extension.Description : "No description provided. Check the repository for more information.";
        lblAuthor.Text = _extension.Author;
        switch (_extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                lblExtensionType.BackColor = Color.FromArgb(0, 95, 173);
                lblExtensionType.Text = LanguageManager.Strings.Plugin;
                break;
            case ExtensionType.IconPack:
                lblExtensionType.BackColor = Color.FromArgb(0, 173, 14);
                lblExtensionType.Text = LanguageManager.Strings.IconPack;
                break;
        }

        CheckInstalled();
    }

    private void CheckInstalled()
    {
        switch (_extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                _installed = _extension.PackageId != null && PluginManager.IsInstalled(_extension.PackageId);
                break;
            case ExtensionType.IconPack:
                _installed = IconManager.IconPacks.Exists(x => x.PackageId == _extension.PackageId && x.ExtensionStoreManaged);
                break;
        }

        if (_installed)
        {
            SetInstalled();
        }
    }

    public async Task LoadIcon(CancellationToken cancellation)
    {
        var iconUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions/icon/{_extension.PackageId}";
        try
        {
            using var iconStream = await FileDownloader.DownloadImageAsync(iconUrl, cancellation);
            var icon = Image.FromStream(iconStream);
            Invoke(() =>
            {
                if (cancellation.IsCancellationRequested || IsDisposed)
                {
                    return;
                }

                extensionIcon.BackgroundImage = icon;
            });
        }
        catch (OperationCanceledException)
        {
            // Task cancelled, ignore
        }
    }

    private void SetInstalled()
    {
        btnInstall.Text = LanguageManager.Strings.Installed;
        btnInstall.Enabled = false;
        btnInstall.BackColor = Color.Transparent;
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        if (_extension.PackageId is null)
        {
            return;
        }

        ExtensionStoreHelper.InstallById(_extension.PackageId);
        CheckInstalled();
    }

    private void LblRepository_LinkClicked(object sender, System.Windows.Forms.LinkLabelLinkClickedEventArgs e)
    {
        if (_extension.GitHubRepository is null)
        {
            return;
        }

        var p = new Process
        {
            StartInfo = new ProcessStartInfo(_extension.GitHubRepository)
            {
                UseShellExecute = true
            }
        };
        p.Start();
    }
}
