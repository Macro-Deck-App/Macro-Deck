namespace SuchByte.MacroDeck.DataTypes.Updater;

public class UpdateApiCheckResult
{
    public bool? NewerVersionAvailable { get; set; }
    public UpdateApiVersionInfo? Version { get; set; }
}