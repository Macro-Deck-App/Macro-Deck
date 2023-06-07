namespace SuchByte.MacroDeck.Models;

public class DownloadProgressInfo
{
    public int Progress { get; set; } = 0;
    public long TotalBytes { get; set; } = 0;
    public long DownloadedBytes { get; set; } = 0;
    public double DownloadSpeed { get; set; } = 0;
}
