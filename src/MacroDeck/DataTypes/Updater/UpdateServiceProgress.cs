namespace SuchByte.MacroDeck.DataTypes.Updater;

public class UpdateServiceProgress
{
    public int Percentage { get; set; } = 0;
    public long TotalBytes { get; set; } = 0;
    public long DownloadedBytes { get; set; } = 0;
}