using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using SuchByte.MacroDeck.DataTypes.FileDownloader;

namespace SuchByte.MacroDeck.Utils;

public class FileDownloader
{
    public static async Task DownloadFileAsync(string url,
        string destinationFileName,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken? cancellationToken = null)
    {
        if (File.Exists(destinationFileName))
        {
            File.Delete(destinationFileName);
        }
        
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(),
            fileStream = new FileStream(destinationFileName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                true);
        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var bytesDownloaded = 0L;
        var buffer = new byte[8192];
        int bytesRead;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0 
               && cancellationToken?.IsCancellationRequested != true)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            bytesDownloaded += bytesRead;

            if (progress == null || totalBytes == -1)
            {
                continue;
            }
            
            var downloadSpeed = bytesDownloaded / stopwatch.Elapsed.TotalSeconds;
            progress.Report(new DownloadProgressInfo
            {
                TotalBytes = totalBytes,
                DownloadedBytes = bytesDownloaded,
                DownloadSpeed = downloadSpeed,
                Percentage = (int)Math.Round((double)bytesDownloaded / totalBytes * 100)
            });
        }
        stopwatch.Stop();
    }
    
    public static async Task<MemoryStream> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var imageStream = new MemoryStream();
        await response.Content.CopyToAsync(imageStream, cancellationToken);
        imageStream.Position = 0;
        
        return imageStream;
    }

}