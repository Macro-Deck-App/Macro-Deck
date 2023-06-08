using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using SuchByte.MacroDeck.DataTypes.FileDownloader;
using SuchByte.MacroDeck.DataTypes.Updater;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Services;

public class UpdateService : IDisposable
{
    // Make UpdateService singleton
    private static UpdateService? _instance;
    public static UpdateService Instance()
    {
        _instance ??= new UpdateService();
        return _instance;
    }
    
    public const PlatformIdentifier PlatformIdentifier = Enums.PlatformIdentifier.WinX64;
    private const string UpdateServiceApiUrl = "https://update.api.macro-deck.app/v2";
    
    public event EventHandler<UpdateApiVersionInfo>? UpdateAvailable;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _checkSemaphoreSlim = new(1);
    private readonly SemaphoreSlim _downloadSemaphoreSlim = new(1);
    
    public UpdateApiVersionInfo? VersionInfo { get; set; }

    public void StartPeriodicalUpdateCheck()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        Task.Run(async() => await DoWork(cancellationToken), cancellationToken);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        GC.SuppressFinalize(this);
    }
    
    public async Task<UpdateApiVersionInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var checkUrl = $"{UpdateServiceApiUrl}/versions/check/{MacroDeck.Version.ToString()}/{PlatformIdentifier}";
        if (MacroDeck.Configuration.UpdateBetaVersions)
        {
            checkUrl += "?betaVersions=true";
        }

        await _checkSemaphoreSlim.WaitAsync(cancellationToken);
        
        using var httpClient = new HttpClient();
        try
        {
            var result =
                await httpClient.GetFromJsonAsync<UpdateApiCheckResult>(checkUrl, cancellationToken: cancellationToken);
            if (result?.NewerVersionAvailable == null)
            {
                throw new InvalidOperationException("Result was null");
            }
        
            if (!result.NewerVersionAvailable.Value && VersionInfo == result.Version)
            {
                return null;
            }

            UpdateAvailable?.Invoke(this, result.Version
                                          ?? throw new InvalidOperationException("Version was null"));

            VersionInfo = result.Version;
            return result.Version;
        }
        finally
        {
            _checkSemaphoreSlim.Release();
        }
    }

    public async ValueTask DownloadAndInstallVersion(
        UpdateApiVersionInfo updateApiVersionInfo,
        IProgress<DownloadProgressInfo> progress)
    {
        await _downloadSemaphoreSlim.WaitAsync();

        try
        {
            var versionFileInfo = updateApiVersionInfo.Platforms?[PlatformIdentifier];
            var destinationPath = 
                await DownloadUpdate(versionFileInfo?.DownloadUrl, updateApiVersionInfo.Version, progress);
            
            await VerifyDownloadedFile(destinationPath, versionFileInfo);
            StartInstallation(destinationPath!);
        }
        finally
        {
            _downloadSemaphoreSlim.Release();
        }
    }

    private static void StartInstallation(string path)
    {
        new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS"
            }
        }.Start();
        MacroDeck.Exit();
    }

    private async ValueTask DoWork(CancellationToken cancellationToken)
    {
        do
        {
            try
            {
                await CheckForUpdatesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error($"Failed to automatically check for updates\n{ex}");
            }
            
            Thread.Sleep(TimeSpan.FromMinutes(30));
        } while (!cancellationToken.IsCancellationRequested);
    }

    private static async ValueTask VerifyDownloadedFile(string? path, UpdateApiVersionFileInfo? updateApiVersionFileInfo)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The path was empty");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The update file was not found");
        }

        await using var ms = File.OpenRead(path);
        var calculatedHash = await ms.CalculateSha256Hash();
        if (!calculatedHash.EqualsCryptographically(updateApiVersionFileInfo?.FileHash))
        {
            throw new InvalidOperationException("The hash of the downloaded file does not match the file on the server");
        }
    }

    private static async ValueTask<string?> DownloadUpdate(string? url, string? version, IProgress<DownloadProgressInfo> progress)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Download url was empty");
        }

        var destinationPath = Path.Combine(ApplicationPaths.TempDirectoryPath, $"update-{version}.exe");
        await FileDownloader.DownloadFileAsync(url, destinationPath, progress);

        return destinationPath;
    }
}