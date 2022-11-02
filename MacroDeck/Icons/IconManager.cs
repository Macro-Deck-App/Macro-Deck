using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Icons;

public class IconManager
{
    public static List<IconPack> IconPacks = new();
    public static List<IconPack> IconPacksUpdateAvailable = new();

    public static event EventHandler InstallationFinished;

    public static event EventHandler OnIconPacksChanged;

    public static Action<object, EventArgs> OnUpdateCheckFinished { get; internal set; }
    public static Action<object, EventArgs> IconPacksLoaded { get; set; }


    public static void Initialize()
    {
        if (!Directory.Exists(MacroDeck.ApplicationPaths.IconPackDirectoryPath))
        {
            Directory.CreateDirectory(MacroDeck.ApplicationPaths.IconPackDirectoryPath);
        }
        LoadIconPacks();
    }

    private static void LoadIconPacks()
    {
        IconPacks.Clear();
        MacroDeckLogger.Info(typeof(IconManager), "Loading icon packs...");
        foreach (var iconPackDir in Directory.GetDirectories(MacroDeck.ApplicationPaths.IconPackDirectoryPath))
        {
            LoadIconPack(iconPackDir);
        }

        MacroDeckLogger.Info(typeof(IconManager), $"Loaded {IconPacks.Count} icon packs");
    }

    public static bool LoadIconPack(string path)
    {
        var extensionManifestFilePath = Path.Combine(path, "ExtensionManifest.json");
        var extensionIconPath = Path.Combine(path, "ExtensionIcon.png");
        if (!File.Exists(extensionManifestFilePath)) return false;
        var extensionManifest = ExtensionManifestModel.FromManifestFile(extensionManifestFilePath);
        if (extensionManifest == null) return false;


        var iconPack = new IconPack
        {
            Name = extensionManifest.Name,
            Author = extensionManifest.Author,
            Version = extensionManifest.Version,
            PackageId = extensionManifest.PackageId,
            ExtensionStoreManaged = File.Exists(Path.Combine(path, ".extensionstore")),
            Hidden = File.Exists(Path.Combine(path, ".hidden")),
            Icons = new List<Icon>(),
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
                    IconId = Path.GetFileNameWithoutExtension(imageFile),
                };
                if (icon.IconId.Equals("ExtensionIcon")) continue;
                iconPack.Icons.Add(icon);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(typeof(IconManager), $"Failed to load icon: {ex.Message}");
            }
        }

        iconPack.IconPackIcon = IconPackPreview.GeneratePreviewImage(iconPack);
        return true;
    }


    public static void ScanUpdatesAsync()
    {
        IconPacksUpdateAvailable.Clear();
        Task.Run(() =>
        {
            foreach (var iconPack in IconPacks.FindAll(iP => iP.ExtensionStoreManaged && !iP.Hidden))
            {
                SearchUpdate(iconPack);
            }

            OnUpdateCheckFinished?.Invoke(null, EventArgs.Empty);
        });
    }

    internal static void SearchUpdate(IconPack iconPack)
    {
        try
        {
            using (var wc = new WebClient())
            {
                var jsonString = wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=check-update&package-id={iconPack.PackageId}&installed-version={iconPack.Version}&target-api={MacroDeck.PluginApiVersion}");
                var jsonObject = JObject.Parse(jsonString);
                var update = (bool)jsonObject["update-available"];
                if (update)
                {
                    MacroDeckLogger.Info("Update available for " + iconPack.Name);
                    IconPacksUpdateAvailable.Add(iconPack);
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        } 
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static IconPack GetIconPackByName(string name)
    {
        return IconPacks.Find(iconPack => iconPack.Name == name);
    }

    public static Icon GetIcon(IconPack iconPack, string iconId)
    {
        return iconPack?.Icons.Find(icon => icon.IconId == iconId);
    }

    public static Icon GetIconByString(string s)
    {
        var iconPack = GetIconPackByName(s.Substring(0, s.IndexOf(".")));
        if (iconPack == null) return null;
        var icon = GetIcon(iconPack, s.Substring(s.IndexOf(".") + 1));
        return icon;
    }

    public static Icon AddIconImage(IconPack iconPack, Image image, string iconId = "")
    {
        if (iconPack == null || image == null || iconPack.Icons.Find(x => x.IconId == iconId) != null) return null;
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
                filePath = Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.gif");
            }
            else
            {
                filePath = Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.png");
                image = new Bitmap(image); // Generating a new bitmap if the file format is not a gif because otherwise it causes a GDI+ error in some cases
                format = ImageFormat.Png;
            }

            image.Save(filePath, format);

            var icon = new Icon
            {
                FilePath = filePath,
                IconId = iconId,
            };

            iconPack.Icons.Add(icon);
                
            return icon;
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error("Failed to add icon to icon pack: " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
        return null;
    }
       
    public static void ExportIconPack(IconPack iconPack, string destination)
    {
        var iconPackDir = Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId);
        try
        {
            iconPack.IconPackIcon?.Save(Path.Combine(iconPackDir, "ExtensionIcon.png"));
            using (var archive = ZipFile.Open(Path.Combine(MacroDeck.ApplicationPaths.BackupsDirectoryPath, destination, $"{iconPack.Name}.macroDeckIconPack"), ZipArchiveMode.Create))
            {
                if (!Directory.Exists(iconPackDir)) return;
                foreach (var iconPackFile in new DirectoryInfo(iconPackDir).GetFiles())
                {
                    archive.CreateEntryFromFile(Path.Combine(iconPackDir, iconPackFile.Name), iconPackFile.Name);
                }
            }
        } catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(IconManager), $"Error while exporting icon pack: {ex.Message}");
        }
            
    }

    public static void DeleteIconPack(IconPack iconPack)
    {
        if (iconPack == null) return;
        if (IconPacks.Contains(iconPack))
        {
            IconPacks.Remove(iconPack);
        }
        OnIconPacksChanged?.Invoke(null, EventArgs.Empty);
        try
        {
            Directory.Delete(Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId), true);
        } catch (Exception ex) 
        {
            MacroDeckLogger.Warning(typeof(IconManager), $"Unable to delete icon pack: {ex.Message}");
        }
    }

    public static void DeleteIcon(IconPack iconPack, Icon icon)
    {
        if (iconPack == null || icon == null) return;
        if (iconPack.Icons.Contains(icon))
        {
            iconPack.Icons.Remove(icon);
        }
        try
        {
            File.Delete(icon.FilePath);
        } catch { }
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
            Version = iconPack.Version,
        };

        var iconPackPath = Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, iconPack.PackageId);

        if (!Directory.Exists(iconPackPath))
        {
            Directory.CreateDirectory(iconPackPath);
        }

        var serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        try
        {
            using var sw = new StreamWriter(Path.Combine(iconPackPath, "ExtensionManifest.json"));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, extensionManifestModel);

            MacroDeckLogger.Info(typeof(IconManager), "ExtensionManifest saved");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(IconManager), $"Failed to save ExtensionManifest: {ex.Message}");
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
            Icons = new List<Icon>(),
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
                MacroDeckLogger.Error(typeof(IconManager), $"{location} does not contain a manifest file!");
                return null;
            }
            if (extensionManifestModel.Type != ExtensionType.IconPack)
            {
                MacroDeckLogger.Error(typeof(IconManager), $"{extensionManifestModel.PackageId} is not a icon pack!");
                return null;
            }
            var destinationPath = Path.Combine(MacroDeck.ApplicationPaths.IconPackDirectoryPath, extensionManifestModel.PackageId);
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
                if (extensionStoreManaged)
                {
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=count-download&package-id={extensionManifestModel.PackageId}");
                        }
                    }
                    catch { }
                }
            } else
            {
                Directory.Delete(destinationPath, true);
            }

            ZipFile.ExtractToDirectory(location, destinationPath);
            if (extensionStoreManaged)
            {
                try
                {
                    File.Create(Path.Combine(destinationPath, ".extensionstore"));
                } catch { }
            }
            if (LoadIconPack(destinationPath))
            {
                if (IconPacksUpdateAvailable.Find(x => x.PackageId.Equals(extensionManifestModel.PackageId)) != null)
                {
                    IconPacksUpdateAvailable.Remove(IconPacksUpdateAvailable.Find(x => x.PackageId.Equals(extensionManifestModel.PackageId)));
                }
                MacroDeckLogger.Info(typeof(IconManager), $"Successfully installed {extensionManifestModel.PackageId}");
                var iconPack = GetIconPackByName(extensionManifestModel.Name);
                InstallationFinished?.Invoke(iconPack, EventArgs.Empty);
                return iconPack;
            }

            MacroDeckLogger.Error(typeof(IconManager), $"{extensionManifestModel.PackageId} is maybe corruped");
            return null;
        } catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(IconManager), $"Error while installing icon pack from zip: {ex.Message}");
        }
        return null;
    }
}