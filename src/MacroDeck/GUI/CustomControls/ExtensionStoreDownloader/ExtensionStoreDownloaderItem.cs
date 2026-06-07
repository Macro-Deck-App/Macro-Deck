using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using SuchByte.MacroDeck.DataTypes.FileDownloader;
using SuchByte.MacroDeck.ExtensionStore;
using Serilog;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.StartupConfig;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionStoreDownloader;

public partial class ExtensionStoreDownloaderItem : RoundedUserControl
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ExtensionStoreDownloaderItem));

    public ExtensionStoreDownloaderPackageInfoModel PackageInfo { get; }

    public ApiV2Extension ApiV2Extension { get; private set; }

    public event EventHandler OnInstallationCompleted;

    private CancellationTokenSource _cancellationTokenSource = new();

    private string _destinationFileName = "";

    public ExtensionStoreDownloaderItem(
        ExtensionStoreDownloaderPackageInfoModel extensionStoreDownloaderPackageInfoModel)
    {
        InitializeComponent();
        PackageInfo = extensionStoreDownloaderPackageInfoModel;
    }

    private async void ExtensionStoreDownloaderItem_Load(object sender, EventArgs e)
    {
        try
        {
            await DownloadAndInstallPackage();
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
            Invoke(() => { lblStatus.Text = LanguageManager.Strings.Error; });
            Cancel();
        }
    }

    public void Cancel()
    {
        _cancellationTokenSource.Cancel();
        Invoke(() =>
        {
            lblStatus.Text = LanguageManager.Strings.Cancelled;
            progressBar.Visible = false;
            btnAbort.Visible = false;
        });

        OnInstallationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task DownloadAndInstallPackage()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        Invoke(() => { lblStatus.Text = LanguageManager.Strings.Preparing; });

        var extensionUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions/{PackageInfo.PackageId}" +
            $"?apiVersion={MacroDeck.PluginApiVersion}" +
            $"&macroDeckVersion={MacroDeck.Version}";
        var fileUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/files/{PackageInfo.PackageId}" +
            $"?apiVersion={MacroDeck.PluginApiVersion}" +
            $"&macroDeckVersion={MacroDeck.Version}";
        using var httpClient = new HttpClient();
        var file = await httpClient.GetFromJsonAsync<ApiV2ExtensionFile>(fileUrl, cancellationToken);
        if (file == null)
        {
            lblStatus.Text = LanguageManager.Strings.Error;
            return;
        }

        var downloadUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/files/download/{PackageInfo.PackageId}" +
            $"?fileVersion={file.Version}";
        var iconUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions/icon/{PackageInfo.PackageId}";
        ApiV2Extension = await httpClient.GetFromJsonAsync<ApiV2Extension>(extensionUrl, cancellationToken) ??
            throw new InvalidOperationException();

        _destinationFileName = Path.Combine(ApplicationPaths.TempDirectoryPath, $"{ApiV2Extension.PackageId}.zip");

        using var iconStream = await FileDownloader.DownloadImageAsync(iconUrl, CancellationToken.None);
        var icon = Image.FromStream(iconStream);

        Invoke(() =>
        {
            extensionIcon.BackgroundImage = icon;
            lblPackageName.Text = string.Format(LanguageManager.Strings.ExtensionStoreDownloaderPackageIdVersion,
                PackageInfo.PackageId,
                "latest");
        });

        await FileDownloader.DownloadFileAsync(downloadUrl,
            _destinationFileName,
            new Progress<DownloadProgressInfo>(UpdateProgress),
            cancellationToken);

        Invoke(() =>
        {
            progressBar.Visible = false;
            lblStatus.Text = LanguageManager.Strings.Installing;
        });

        await Install(file.FileHash, cancellationToken);
    }

    private void UpdateProgress(DownloadProgressInfo progressInfo)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<DownloadProgressInfo>(UpdateProgress), progressInfo);
            return;
        }

        progressBar.Visible = true;
        lblStatus.Text =
            $@"{progressInfo.DownloadedBytes / (1024.0 * 1024.0):0.00} MB " +
            $@"/ {progressInfo.TotalBytes / (1024.0 * 1024.0):0.00} MB " +
            $@"@ {progressInfo.DownloadSpeed / (1024.0 * 1024.0):0.00} MB/s";
        progressBar.Progress = (int)(progressInfo.DownloadedBytes * 100L / progressInfo.TotalBytes);
    }

    private async Task Install(string expectedFileHash, CancellationToken cancellationToken)
    {
        if (!File.Exists(_destinationFileName))
        {
            Finished();
            Logger.Error(
                $"Download of {ApiV2Extension.Name} failed: {ApiV2Extension.PackageId ?? "unknown.zip"} not found");
            return;
        }

        var hash = await Sha256Utils.CalculateSha256Hash(_destinationFileName);
        if (!expectedFileHash.Equals(hash))
        {
            Finished();
            Logger.Error(
                $"Checksum of {ApiV2Extension.Name} not matching!" +
                Environment.NewLine +
                $"Checksum on server: {expectedFileHash}" +
                Environment.NewLine +
                $"Checksum of downloaded file: {hash}");
            return;
        }

        Logger.Information($"Start installation of {ApiV2Extension.Name} version latest");


        ExtensionManifestModel extensionManifestModel;
        try
        {
            extensionManifestModel = ExtensionManifestModel.FromZipFilePath(_destinationFileName) ??
                throw new InvalidOperationException();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while deserializing ExtensionManifest for {ExtensionName}", ApiV2Extension.Name);
            Finished();
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        switch (extensionManifestModel.Type)
        {
            case ExtensionType.Plugin:
                try
                {
                    PluginManager.InstallPluginFromZip(_destinationFileName);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while installing {ExtensionName}", ApiV2Extension.Name);
                    Finished();
                    return;
                }

                break;
            case ExtensionType.IconPack:
                try
                {
                    IconManager.InstallIconPackZip(_destinationFileName, true);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while installing {ExtensionName}", ApiV2Extension.Name);
                    Finished();
                    return;
                }

                break;
        }

        try
        {
            if (Directory.Exists(
                Path.Combine(ApplicationPaths.TempDirectoryPath, ApiV2Extension.PackageId ?? "unknown")))
            {
                Directory.Delete(
                    Path.Combine(ApplicationPaths.TempDirectoryPath, ApiV2Extension.PackageId ?? "unknown"),
                    true);
            }

            if (File.Exists(_destinationFileName))
            {
                File.Delete(_destinationFileName);
            }
        }
        catch
        {
        }

        Finished();
    }

    private void Finished(bool error = false)
    {
        Invoke(() =>
        {
            btnAbort.Visible = false;
            lblStatus.Text = error ? LanguageManager.Strings.Error : LanguageManager.Strings.Completed;
        });
        OnInstallationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void BtnAbort_Click(object sender, EventArgs e)
    {
        Cancel();
    }
}
