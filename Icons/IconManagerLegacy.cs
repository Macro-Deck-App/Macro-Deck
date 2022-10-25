using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Utils;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.Icons
{
    [Obsolete("Will be replaced with IconManager")]
    public static class IconManagerLegacy
    {
        public static List<IconPackLegacy> IconPacks = new();

        public static List<IconPackLegacy> IconPacksUpdateAvailable = new();

        public static event EventHandler OnUpdateCheckFinished;

        public static event EventHandler IconPacksLoaded;

        public static void ConvertOldIconPacks()
        {
            if (Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.iconpack").Length == 0) return;
            var iconPacks = new List<IconPackLegacy>();
            using (var msgBox = new MessageBox())
            {
                if (msgBox.ShowDialog("Icon packs", "Macro Deck has found old icon packs that need to be converted to continue using. Convert now?", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }
            }
            foreach (var databasePath in Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.iconpack"))
            {
                try
                {
                    var db = new SQLiteConnection(databasePath);
                    var query = db.Table<IconPackJson>();

                    var jsonString = query.First().JsonString;
                    var iconPack = JsonConvert.DeserializeObject<IconPackLegacy>(jsonString, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) => { args.ErrorContext.Handled = true; }
                    });

                    iconPacks.Add(iconPack);


                    db.Close();
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error(string.Format("Failed to load icon pack {0}: ", databasePath) + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }
            foreach (var iconPack in iconPacks)
            {
                var extensionManifestModel = new ExtensionManifestModel
                {
                    Type = ExtensionType.IconPack,
                    Name = iconPack.Name,
                    Author = iconPack.Author,
                    PackageId = $"{iconPack.Author.Replace(" ", "").Replace(".", "")}.{iconPack.Name.Replace(" ", "").Replace(".", "")}",
                    TargetPluginAPIVersion = MacroDeck.PluginApiVersion,
                    Version = iconPack.Version,
                };

                if (!Directory.Exists(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId)))
                {
                    Directory.CreateDirectory(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId));
                }

                if (iconPack.PackageManagerManaged)
                {
                    try
                    {
                        File.Create(Path.Combine(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId, ".extensionstore")));
                    }catch { }
                }

                if (iconPack.Hidden)
                {
                    try
                    {
                        File.Create(Path.Combine(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId, ".hidden")));
                    }
                    catch { }
                }

                var serializer = new JsonSerializer
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented,
                };

                try
                {
                    using var sw = new StreamWriter(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId, "ExtensionManifest.json"));
                    using JsonWriter writer = new JsonTextWriter(sw);
                    serializer.Serialize(writer, extensionManifestModel);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error(typeof(IconManagerLegacy), "Failed to save ExtensionManifest.json: " + ex.Message);
                    continue;
                }

                foreach (var icon in iconPack.Icons)
                {
                    try
                    {
                        var iconImage = Base64.GetImageFromBase64(icon.IconBase64);
                        var format = iconImage.RawFormat;
                        if (format.ToString().Equals("Gif", StringComparison.OrdinalIgnoreCase))
                        {
                            iconImage.Save(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId, $"{icon.IconId}.gif"));
                        }
                        else
                        {
                            iconImage = new Bitmap(iconImage); // Generating a new bitmap if the file format is not a gif because otherwise it causes a GDI+ error in some cases
                            format = ImageFormat.Png;
                            iconImage.Save(Path.Combine(MacroDeck.IconPackDirectoryPath, extensionManifestModel.PackageId, $"{icon.IconId}.png"), format);
                        }
                    } catch
                    {
                    }
                }
            }

            using (var msgBox = new MessageBox())
            {
                if (msgBox.ShowDialog("Icon packs", "The old icon packs were successfully converted. Do you want to delete the old icon packs?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    foreach (var databasePath in Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.iconpack"))
                    {
                        try
                        {
                            File.Delete(databasePath);
                        }
                        catch (Exception ex)
                        {
                            MacroDeckLogger.Error(string.Format("Failed to delete icon pack {0}: ", databasePath) + ex.Message + Environment.NewLine + ex.StackTrace);
                        }
                    }
                }
            }
        }

       /* public static void LoadIconPacks()
        {
            MacroDeckLogger.Info("Loading icon packs...");
            IconPacks.Clear();
            IconPacksUpdateAvailable.Clear();

            if (!Directory.Exists(MacroDeck.IconPackDirectoryPath))
            {
                Directory.CreateDirectory(MacroDeck.IconPackDirectoryPath);
            }
            else
            {
                // Change the file extension from icon packs of older versions
                foreach (var databasePath in Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.db"))
                {
                    try
                    {
                        var newFileName = Path.Combine(MacroDeck.IconPackDirectoryPath, Path.GetFileNameWithoutExtension(databasePath) + ".iconpack");

                        // Delete the existing file if exists
                        if (File.Exists(newFileName))
                        {
                            File.Delete(newFileName); 
                        }
                        File.Move(databasePath, newFileName);
                    } catch {}
                }
            }

            foreach (var databasePath in Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.iconpack"))
            {
                try
                {
                    var db = new SQLiteConnection(databasePath);
                    var query = db.Table<IconPackJson>();

                    string jsonString = query.First().JsonString;
                    IconPackLegacy iconPack = JsonConvert.DeserializeObject<IconPackLegacy>(jsonString, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) => { args.ErrorContext.Handled = true; }
                    });

                    IconPacks.Add(iconPack);
                    if (iconPack.PackageManagerManaged && !iconPack.Hidden)
                    {
                        Task.Run(() =>
                            SearchUpdate(iconPack)
                        );
                    }

                    db.Close();
                } catch (Exception ex) 
                {
                    MacroDeckLogger.Error(String.Format("Failed to load icon pack {0}: ", databasePath) + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }

            if (IconPacks.FindAll(x => x.PackageManagerManaged == false && x.Hidden == false).Count == 0)
            {
                MacroDeckLogger.Info("No icon packs found. Creating a new one");
                IconPackLegacy newIconPack = new IconPackLegacy
                {
                    Author = Environment.UserName,
                    Name = "New icon pack",
                    Version = "1.0.0",
                    Icons = new List<Icon>()
                };
                IconPacks.Add(newIconPack);
                AddIconImage(newIconPack, Resources.Icon);
            }

            MacroDeckLogger.Info(String.Format("Loaded {0} icon pack(s)", IconPacks.Count));

            if (IconPacksLoaded != null)
            {
                IconPacksLoaded(IconPacks, EventArgs.Empty);
            }
        }

        [Obsolete]
        public static IconPackLegacy AddFromFile(string path)
        {
            IconPackLegacy iconPack = null;
            var db = new SQLiteConnection(path);
            var query = db.Table<IconPackJson>();

            foreach (var iconPackJson in query)
            {
                string jsonString = iconPackJson.JsonString;
                iconPack = JsonConvert.DeserializeObject<IconPackLegacy>(jsonString, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                });
                IconPacks.Add(iconPack);
            }

            db.Close();

            return iconPack;
        }

        public static void SaveIconPacks()
        {   
            foreach (IconPackLegacy iconPack in IconPacks)
            {
                SaveIconPack(iconPack);
            }
        }

         public static void ScanUpdatesAsync()
         {
             IconPacksUpdateAvailable.Clear();
             Task.Run(() =>
             {
                 foreach (IconPackLegacy iconPack in IconPacks.FindAll(iP => iP.PackageManagerManaged && !iP.Hidden))
                 {
                     SearchUpdate(iconPack);
                 }
                 if (OnUpdateCheckFinished != null)
                 {
                     OnUpdateCheckFinished(null, EventArgs.Empty);
                 }
             });
         }

         internal static void SearchUpdate(IconPackLegacy iconPack)
         {
             try
             {
                 using (WebClient wc = new WebClient())
                 {
                     var jsonString = wc.DownloadString($"https://macrodeck.org/extensionstore/extensionstore.php?action=check-update&package-id={ExtensionStoreHelper.GetPackageId(iconPack)}&installed-version={iconPack.Version}&target-api={MacroDeck.PluginApiVersion}");
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
         


        public static void SaveIconPack(IconPackLegacy iconPack, string directory = null)
        {
            SQLiteConnection db;
            if (directory == null)
            {
               db = new SQLiteConnection(Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.Name + ".iconpack"));
            } else
            {
                // Export
                db = new SQLiteConnection(Path.Combine(directory, iconPack.Name.ToLower().Replace(' ', '-') + "-" + iconPack.Version + ".iconpack"));
            }
            db.CreateTable<IconPackJson>();
            db.DeleteAll<IconPackJson>();

            string jsonString = JsonConvert.SerializeObject(iconPack, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            });

            IconPackJson iconPackJson = new IconPackJson
            {
                JsonString = jsonString
            };

            db.InsertOrReplace(iconPackJson);
            db.Close();
        }


        public static void CreateIconPack(string name, string author, string version)
        {
            IconPackLegacy iconPack = new IconPackLegacy
            {
                Name = name.Replace(".", "")
            };
            ;
            iconPack.Author = author;
            iconPack.Version = version;
            iconPack.Icons = new List<Icon>();
            IconPacks.Add(iconPack);
            SaveIconPacks();
            MacroDeckServer.SendAllIcons();
        }

        public static void EditIconPack(IconPackLegacy iconPack, string author, string version)
        {
            IconPacks.Remove(iconPack);
            iconPack.Author = author;
            iconPack.Version = version;
            IconPacks.Add(iconPack);
            SaveIconPacks();
            MacroDeckServer.SendAllIcons();
        }

        public static void DeleteIconPack(IconPackLegacy iconPack)
        {
            MacroDeckLogger.Info("Deleting icon pack " + iconPack.Name);
            IconPacks.Remove(iconPack);
            
            try
            {
                File.Delete(Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.Name + ".iconpack"));
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(String.Format("Failed to delete icon pack {0}: ", iconPack.Name) + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            if (IconPacks.FindAll(x => x.PackageManagerManaged == false && x.Hidden == false).Count == 0)
            {
                MacroDeckLogger.Info("No icon packs found. Creating a new one");
                IconPackLegacy newIconPack = new IconPackLegacy
                {
                    Author = Environment.UserName,
                    Name = "New icon pack",
                    Version = "1.0.0",
                    Icons = new List<Icon>()
                };
                IconPacks.Add(newIconPack);
                AddIconImage(newIconPack, Resources.Icon);
            }
            MacroDeckLogger.Info("Icon pack successfully deleted");
            MacroDeckServer.SendAllIcons();
        }

        public static IconPackLegacy GetIconPackByName(string name)
        {
            return IconPacks.Find(iconPack => iconPack.Name.Equals(name));
        }

        public static Icon GetIcon(IconPackLegacy iconPack, long iconId)
        {
            if (iconPack == null) return null;
            return iconPack.Icons.Find(icon => icon.IconId.Equals(iconId));
        }

        public static Icon AddIconImage(IconPackLegacy iconPack, Image image, bool sendIconsToClients = true)
        {
            try
            {
                var base64 = Utils.Base64.GetBase64FromImage(image);
                Icon icon = new Icon
                {
                };
                iconPack.Icons.Add(icon);
                SaveIconPack(iconPack);
                if (sendIconsToClients)
                {
                    MacroDeckServer.SendAllIcons();
                }
                return icon;
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Failed to add icon to icon pack: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            return null;
        }


        public static void DeleteIcon(IconPackLegacy iconPack, Icon icon)
        {
            if (!iconPack.Icons.Contains(icon)) return;
            IconPacks[IconPacks.IndexOf(iconPack)].Icons.Remove(icon);

            MacroDeckLogger.Info("Deleted icon from " + iconPack.Name);

            SaveIconPack(iconPack);
            MacroDeckServer.SendAllIcons();
        }

        */

    }
}
