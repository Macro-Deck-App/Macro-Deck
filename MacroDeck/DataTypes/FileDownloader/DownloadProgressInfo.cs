namespace SuchByte.MacroDeck.DataTypes.FileDownloader;

public class DownloadProgressInfo
{
    public int Percentage { get; set; } = 0;
    public long TotalBytes { get; set; } = 0;
    public long DownloadedBytes { get; set; } = 0;
    public double DownloadSpeed { get; set; } = 0;
}