using SuchByte.MacroDeck.Enums;

namespace SuchByte.MacroDeck.DataTypes.Updater;

public class UpdateApiVersionInfo
{
    public string? Version { get; set; }
    public bool? IsBeta { get; set; }
    public string? ChangeNotesUrl { get; set; }
    public Dictionary<PlatformIdentifier, UpdateApiVersionFileInfo>? Platforms { get; set; }
}