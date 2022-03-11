﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Icons
{
    public class IconManager
    {
        public static List<IconPack> IconPacks = new List<IconPack>();
        public static List<IconPack> IconPacksUpdateAvailable = new List<IconPack>();

        public static Action<object, EventArgs> OnUpdateCheckFinished { get; internal set; }
        public static Action<object, EventArgs> IconPacksLoaded { get; internal set; }

        public static void Initialize()
        {
            if (!Directory.Exists(MacroDeck.IconPackDirectoryPath))
            {
                Directory.CreateDirectory(MacroDeck.IconPackDirectoryPath);
            }
            LoadIconPacks();
        }

        private static void LoadIconPacks()
        {
            IconPacks.Clear();
            MacroDeckLogger.Info(typeof(IconManager), "Loading icon packs...");
            foreach (var iconPackDir in Directory.GetDirectories(MacroDeck.IconPackDirectoryPath))
            {
                var extensionManifestFilePath = Path.Combine(iconPackDir, "ExtensionManifest.json");
                var extensionIconPath = Path.Combine(iconPackDir, "ExtensionIcon.png");
                if (!File.Exists(extensionManifestFilePath)) continue;
                var extensionManifest = ExtensionManifestModel.FromManifestFile(extensionManifestFilePath);
                if (extensionManifest == null) continue;


                IconPack iconPack = new IconPack()
                {
                    Name = extensionManifest.Name,
                    Author = extensionManifest.Author,
                    Version = extensionManifest.Version,
                    PackageId = extensionManifest.PackageId,
                    ExtensionStoreManaged = File.Exists(Path.Combine(iconPackDir, ".extensionstore")),
                    Hidden = File.Exists(Path.Combine(iconPackDir, ".hidden")),
                    Icons = new List<Icon>(),
                };

                IconPacks.Add(iconPack);
                
                foreach (var imageFile in Directory.GetFiles(iconPackDir).Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                        s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                                                                                        s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        Icon icon = new Icon()
                        {
                            FilePath = imageFile,
                            IconId = Path.GetFileNameWithoutExtension(imageFile),
                        };
                        iconPack.Icons.Add(icon);
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning(typeof(IconManager), $"Failed to load icon: {ex.Message}");
                        continue;
                    }
                }

                iconPack.IconPackIcon = Utils.IconPackPreview.GeneratePreviewImage(iconPack);
            }

            MacroDeckLogger.Info(typeof(IconManager), $"Loaded {IconPacks.Count} icon packs");
        }


        public static void ScanUpdatesAsync()
        {
            IconPacksUpdateAvailable.Clear();
            Task.Run(() =>
            {
                foreach (IconPack iconPack in IconPacks.FindAll(iP => iP.ExtensionStoreManaged && !iP.Hidden))
                {
                    SearchUpdate(iconPack);
                }
                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
                }
            });
        }

        internal static void SearchUpdate(IconPack iconPack)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var jsonString = wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=check-update&package-id={iconPack.PackageId}&installed-version={iconPack.Version}&target-api={MacroDeck.PluginApiVersion}");
                    JObject jsonObject = JObject.Parse(jsonString);
                    bool update = (bool)jsonObject["update-available"];
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
            catch { }
        }

        public static IconPack GetIconPackByName(string name)
        {
            return IconPacks.Find(iconPack => iconPack.Name == name);
        }

        public static Icon GetIcon(IconPack iconPack, string iconId)
        {
            if (iconPack == null) return null;
            return iconPack.Icons.Find(icon => icon.IconId == iconId);
        }

        public static Icon GetIconByString(string s)
        {
            IconPack iconPack = GetIconPackByName(s.Substring(0, s.IndexOf(".")));
            if (iconPack == null) return null;
            Icon icon = GetIcon(iconPack, s.Substring(s.IndexOf(".") + 1));
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
                    filePath = Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.gif");
                }
                else
                {
                    filePath = Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.PackageId, $"{iconId}.png");
                    image = new Bitmap(image); // Generating a new bitmap if the file format is not a gif because otherwise it causes a GDI+ error in some cases
                    format = ImageFormat.Png;
                }

                image.Save(filePath, format);

                Icon icon = new Icon()
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
            string iconPackDir = Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.PackageId);
            try
            {
                iconPack.IconPackIcon.Save(Path.Combine(iconPackDir, "ExtensionIcon.png"));
                using (var archive = ZipFile.Open(Path.Combine(MacroDeck.BackupsDirectoryPath, destination, $"{iconPack.Name}.zip"), ZipArchiveMode.Create))
                {
                    if (!Directory.Exists(iconPackDir)) return;
                    foreach (FileInfo iconPackFile in new DirectoryInfo(iconPackDir).GetFiles())
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
            try
            {
                Directory.Delete(Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.PackageId), true);
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
            ExtensionManifestModel extensionManifestModel = new ExtensionManifestModel()
            {
                Type = ExtensionStore.ExtensionType.IconPack,
                Name = iconPack.Name,
                Author = iconPack.Author,
                PackageId = iconPack.PackageId,
                TargetPluginAPIVersion = MacroDeck.PluginApiVersion,
                Version = iconPack.Version,
            };

            string iconPackPath = Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.PackageId);

            if (!Directory.Exists(iconPackPath))
            {
                Directory.CreateDirectory(iconPackPath);
            }

            JsonSerializer serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            try
            {
                using StreamWriter sw = new StreamWriter(Path.Combine(iconPackPath, "ExtensionManifest.json"));
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
            var iconPack = new IconPack()
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
    }
}
