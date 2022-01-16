using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Properties;
using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using SuchByte.MacroDeck.Server;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Icons
{
    public static class IconManager
    {
        public static List<IconPack> IconPacks = new List<IconPack>();
        public static List<IconPack> IconPacksUpdateAvailable = new List<IconPack>();

        public static event EventHandler OnUpdateCheckFinished;

        public static event EventHandler IconPacksLoaded;

        public static void LoadIconPacks()
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
                    IconPack iconPack = JsonConvert.DeserializeObject<IconPack>(jsonString, new JsonSerializerSettings
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
                IconPack newIconPack = new IconPack
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
        public static IconPack AddFromFile(string path)
        {
            IconPack iconPack = null;
            var db = new SQLiteConnection(path);
            var query = db.Table<IconPackJson>();

            foreach (var iconPackJson in query)
            {
                string jsonString = iconPackJson.JsonString;
                iconPack = JsonConvert.DeserializeObject<IconPack>(jsonString, new JsonSerializerSettings
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
            foreach (IconPack iconPack in IconPacks)
            {
                SaveIconPack(iconPack);
            }
        }

        public static void ScanUpdatesAsync()
        {
            IconPacksUpdateAvailable.Clear();
            Task.Run(() =>
            {
                foreach (IconPack iconPack in IconPacks.FindAll(iP => iP.PackageManagerManaged && !iP.Hidden))
                {
                    SearchUpdate(iconPack);
                }
            });
        }

        private static void SearchUpdate(IconPack iconPack)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?action=check-version&name=" + iconPack.Name + "&version=" + iconPack.Version + "&target-api=" + MacroDeck.PluginApiVersion);
                    JObject jsonObject = JObject.Parse(jsonString);
                    bool update = (bool)jsonObject["newer-version-available"];
                    if (update)
                    {
                        MacroDeckLogger.Info("Update available for " + iconPack.Name);
                        IconPacksUpdateAvailable.Add(iconPack);
                    }
                }
                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }



        public static void SaveIconPack(IconPack iconPack, string directory = null)
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
            IconPack iconPack = new IconPack
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

        public static void EditIconPack(IconPack iconPack, string author, string version)
        {
            IconPacks.Remove(iconPack);
            iconPack.Author = author;
            iconPack.Version = version;
            IconPacks.Add(iconPack);
            SaveIconPacks();
            MacroDeckServer.SendAllIcons();
        }

        public static void DeleteIconPack(IconPack iconPack)
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
                IconPack newIconPack = new IconPack
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

        public static IconPack GetIconPackByName(string name)
        {
            return IconPacks.Find(iconPack => iconPack.Name.Equals(name));
        }

        public static Icon GetIcon(IconPack iconPack, long iconId)
        {
            if (iconPack == null) return null;
            return iconPack.Icons.Find(icon => icon.IconId.Equals(iconId));
        }

        public static Icon AddIconImage(IconPack iconPack, Image image, bool sendIconsToClients = true)
        {
            try
            {
                var base64 = Utils.Base64.GetBase64FromImage(image);
                Icon icon = new Icon
                {
                    IconBase64 = base64,
                    IconId = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    IconPack = iconPack.Name
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

        public static void DeleteIcon(IconPack iconPack, Icon icon)
        {
            if (!iconPack.Icons.Contains(icon)) return;
            IconPacks[IconPacks.IndexOf(iconPack)].Icons.Remove(icon);

            MacroDeckLogger.Info("Deleted icon from " + iconPack.Name);

            SaveIconPack(iconPack);
            MacroDeckServer.SendAllIcons();
        }



    }
}
