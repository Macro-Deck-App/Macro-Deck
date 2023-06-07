﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SuchByte.MacroDeck.Models;

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
                DownloadSpeed = downloadSpeed
            });
        }
        stopwatch.Stop();
    }
    
    public static async Task<MemoryStream> DownloadImageAsync(string url)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var imageStream = new MemoryStream();
        await response.Content.CopyToAsync(imageStream);
        imageStream.Position = 0;
        
        return imageStream;
    }

}