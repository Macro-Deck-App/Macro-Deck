using SuchByte.MacroDeck.ExtensionStore;

namespace SuchByte.MacroDeck.Models;

public class ExtensionStoreDownloaderPackageInfoModel
{
    public string PackageId { get; set; } = "";
    public ExtensionType ExtensionType { get; set; }
}
