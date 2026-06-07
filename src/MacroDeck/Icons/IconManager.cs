using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using Newtonsoft.Json;
using Serilog;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.StartupConfig;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Icons;

public class IconManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(IconManager));

    public static List<IconPack> IconPacks = new();
    public static List<IconPack> IconPacksUpdateAvailable = new();

    public static event EventHandler InstallationFinished;

    public static event EventHandler OnIconPacksChanged;

    public static Action<object, EventArgs> OnUpdateCheckFinished { get; internal set; }
    public static Action<object, EventArgs> IconPacksLoaded { get; set; }


    public static void Initialize()
    {
        if (!Directory.Exists(ApplicationPaths.IconPackDirectoryPath))
        {
            Directory.CreateDirectory(ApplicationPaths.IconPackDirectoryPath);
        }

        LoadIconPacks();
    }

    private static void LoadIconPacks()
    {
        IconPacks.Clear();
        Logger.Information("Loading icon packs...");
        foreach (var iconPackDir in Directory.GetDirectories(ApplicationPaths.IconPackDirectoryPath))
        {
            LoadIconPack(iconPackDir);
        }

        if (IconPacks.Count == 0)
        {
            CreateIconPack("My Icons", Environment.UserName, "1.0.0");
        }

        Logger.Information("Loaded {IconPackCount} icon packs", IconPacks.Count);
    }

    public static bool LoadIconPack(string path)
    {
        var extensionManifestFilePath = Path.Combine(path, "ExtensionManifest.json");
        var extensionIconPath = Path.Combine(path, "ExtensionIcon.png");
        if (!File.Exists(extensionManifestFilePath))
        {
            return false;
        }

        var extensionManifest = ExtensionManifestModel.FromManifestFile(extensionManifestFilePath);
        if (extensionManifest == null)
        {
            return false;
        }


        var iconPack = new IconPack
        {
            Name = extensionManifest.Name,
            Author = extensionManifest.Author,
            Version = extensionManifest.Version,
            PackageId = extensionManifest.PackageId,
            ExtensionStoreManaged = File.Exists(Path.Combine(path, ".extensionstore")),
            Hidden = File.Exists(Path.Combine(path, ".hidden")),
            Icons = new List<Icon>()
        };

        if (IconPacks.Find(x => x.PackageId.Equals(iconPack.PackageId)) != null)
        {
            IconPacks.RemoveAll(x => x.PackageId.Equals(iconPack.PackageId));
        }

        IconPacks.Add(iconPack);

        foreach (var imageFile in Directory.GetFiles(path).Where(s =>
            s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var icon = new Icon
                {
                    FilePath = imageFile,
                    IconId = Path.GetFileNameWithoutExtension(imageFile)
                };
                if (icon.IconId.Equals("ExtensionIcon"))
                {
                    continue;
                }

                iconPack.Icons.Add(icon);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to load icon");
            }
        }

        iconPack.IconPackIcon = IconPackPreview.GeneratePreviewImage(iconPack);
        return true;
    }

    internal static async Task SearchUpdate(IconPack iconPack)
    {
        var updateAvailable = await ExtensionStoreHelper.CheckForAvailableUpdate(iconPack.PackageId, iconPack.Version);
        if (updateAvailable)
        {
            IconPacksUpdateAvailable.Add(iconPack);
        }
    }

    public static IconPack? GetIconPackByName(string name)
    {
        return IconPacks.FirstOrDefault(iconPack => iconPack.Name == name);
    }

    public static Icon? GetIcon(IconPack iconPack, string iconId)
    {
        return iconPack?.Icons.FirstOrDefault(icon => icon.IconId == iconId);
    }

    public static Icon? GetIconByString(string s)
    {
        var iconPack = GetIconPackByName(s.Substring(0, s.IndexOf(".")));
        if (iconPack == null)
        {
            return null;
        }

        var icon = GetIcon(iconPack, s.Substring(s.IndexOf(".") + 1));
        return icon;
    }

    public static Icon AddIconImage(IconPack iconPack, Image image, string iconId = "")
    {
        if (iconPack == null || image == null || iconPack.Icons.Find(x => x.IconId == iconId) != null)
        {
            return null;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(iconId))
            {
                iconId = Guid.NewGuid().ToString();
            }

            var format = image.RawFormat;
            var filePath = "";
            if (format.ToString().Equals("Gif", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.Combine(ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.gif");
            }
            else
            {
                filePath = Path.Combine(ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.png");
                image = new Bitmap(
                    image); // Generating a new bitmap if the file format is not a gif because otherwise it causes a GDI+ error in some cases
                format = ImageFormat.Png;
            }

            image.Save(filePath, format);

            var icon = new Icon
            {
                FilePath = filePath,
                IconId = iconId
            };

            iconPack.Icons.Add(icon);

            return icon;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to add icon to icon pack");
        }

        return null;
    }

    public static void ExportIconPack(IconPack iconPack, string destination)
    {
        var iconPackDir = Path.Combine(ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId);
        try
        {
            iconPack.IconPackIcon?.Save(Path.Combine(iconPackDir, "ExtensionIcon.png"));
            using var archive = ZipFile.Open(Path.Combine(ApplicationPaths.BackupsDirectoryPath,
                    destination,
                    $"{iconPack.Name}.macroDeckIconPack"),
                ZipArchiveMode.Create);
            if (!Directory.Exists(iconPackDir))
            {
                return;
            }

            foreach (var iconPackFile in new DirectoryInfo(iconPackDir).GetFiles())
            {
                archive.CreateEntryFromFile(Path.Combine(iconPackDir, iconPackFile.Name), iconPackFile.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while exporting icon pack");
        }
    }

    public static void DeleteIconPack(IconPack iconPack)
    {
        if (iconPack == null)
        {
            return;
        }

        if (IconPacks.Contains(iconPack))
        {
            IconPacks.Remove(iconPack);
        }

        OnIconPacksChanged?.Invoke(null, EventArgs.Empty);
        try
        {
            Directory.Delete(Path.Combine(ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId), true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to delete icon pack");
        }
    }

    public static void DeleteIcon(IconPack iconPack, Icon icon)
    {
        if (iconPack == null || icon == null)
        {
            return;
        }

        if (iconPack.Icons.Contains(icon))
        {
            iconPack.Icons.Remove(icon);
        }

        try
        {
            File.Delete(icon.FilePath);
        }
        catch
        {
        }
    }

    public static void SaveIconPack(IconPack iconPack)
    {
        var extensionManifestModel = new ExtensionManifestModel
        {
            Type = ExtensionType.IconPack,
            Name = iconPack.Name,
            Author = iconPack.Author,
            PackageId = iconPack.PackageId,
            TargetPluginAPIVersion = MacroDeck.PluginApiVersion,
            Version = iconPack.Version
        };

        var iconPackPath = Path.Combine(ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId);

        if (!Directory.Exists(iconPackPath))
        {
            Directory.CreateDirectory(iconPackPath);
        }

        var serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        try
        {
            using var sw = new StreamWriter(Path.Combine(iconPackPath, "ExtensionManifest.json"));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, extensionManifestModel);

            Logger.Information("ExtensionManifest saved");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save ExtensionManifest");
        }
    }

    public static void CreateIconPack(string iconPackName, string author, string version)
    {
        var iconPack = new IconPack
        {
            Name = iconPackName,
            Author = author,
            Version = version,
            PackageId = $"{author.Replace(" ", "").Replace(".", "")}.{iconPackName.Replace(" ", "").Replace(".", "")}",
            Icons = new List<Icon>()
        };

        SaveIconPack(iconPack);

        IconPacks.Add(iconPack);
    }

    public static IconPack InstallIconPackZip(string location, bool extensionStoreManaged = false)
    {
        try
        {
            var extensionManifestModel = ExtensionManifestModel.FromZipFilePath(location);
            if (extensionManifestModel == null)
            {
                Logger.Error("{Location} does not contain a manifest file!", location);
                return null;
            }

            if (extensionManifestModel.Type != ExtensionType.IconPack)
            {
                Logger.Error("{PackageId} is not a icon pack!", extensionManifestModel.PackageId);
                return null;
            }

            var destinationPath
                = Path.Combine(ApplicationPaths.IconPackDirectoryPath, extensionManifestModel.PackageId);
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
                if (extensionStoreManaged)
                {
                    try
                    {
                        using var wc = new WebClient();
                        wc.DownloadString(
                            $"https://macrodeck.org/extensionstore/extensionstore.php?action=count-download&package-id={extensionManifestModel.PackageId}");
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                Directory.Delete(destinationPath, true);
            }

            ZipFile.ExtractToDirectory(location, destinationPath);
            if (extensionStoreManaged)
            {
                try
                {
                    File.Create(Path.Combine(destinationPath, ".extensionstore"));
                }
                catch
                {
                }
            }

            if (LoadIconPack(destinationPath))
            {
                if (IconPacksUpdateAvailable.Find(x => x.PackageId.Equals(extensionManifestModel.PackageId)) != null)
                {
                    IconPacksUpdateAvailable.Remove(IconPacksUpdateAvailable.Find(x =>
                        x.PackageId.Equals(extensionManifestModel.PackageId)));
                }

                Logger.Information("Successfully installed {PackageId}", extensionManifestModel.PackageId);
                var iconPack = GetIconPackByName(extensionManifestModel.Name);
                InstallationFinished?.Invoke(iconPack, EventArgs.Empty);
                return iconPack;
            }

            Logger.Error("{PackageId} is maybe corruped", extensionManifestModel.PackageId);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while installing icon pack from zip");
        }

        return null;
    }
}
