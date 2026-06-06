namespace SuchByte.MacroDeck.Backup;

public class RestoreBackupInfo
{
    public bool RestoreConfig { get; set; } = false;
    public bool RestoreProfiles { get; set; } = false;
    public bool RestoreDevices { get; set; } = false;
    public bool RestoreVariables { get; set; } = false;
    public bool RestorePlugins { get; set; } = false;
    public bool RestorePluginConfigs { get; set; } = false;
    public bool RestorePluginCredentials { get; set; } = false;
    public bool RestoreIconPacks { get; set; } = false;

}