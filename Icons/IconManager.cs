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

namespace SuchByte.MacroDeck.Icons
{
    public static class IconManager
    {
        public static List<IconPack> IconPacks = new List<IconPack>();
        public static List<IconPack> IconPacksUpdateAvailable = new List<IconPack>();

        public static event EventHandler OnUpdateCheckFinished;

        /*public IconManager()
        {
            LoadIconPacks();
        }*/


        public static void LoadIconPacks()
        {
            IconPacks.Clear();
            IconPacksUpdateAvailable.Clear();

            if (!Directory.Exists(MacroDeck.IconPackDirectoryPath))
            {
                Directory.CreateDirectory(MacroDeck.IconPackDirectoryPath);
            }

            foreach (var databasePath in Directory.GetFiles(MacroDeck.IconPackDirectoryPath, "*.db"))
            {
                var db = new SQLiteConnection(databasePath);
                var query = db.Table<IconPackJson>();

                foreach (var iconPackJson in query)
                {
                    string jsonString = iconPackJson.JsonString;
                    IconPack iconPack = JsonConvert.DeserializeObject<IconPack>(jsonString, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) => { args.ErrorContext.Handled = true; }
                    });

                    IconPacks.Add(iconPack);
                    Task.Run(() =>
                        SearchUpdate(iconPack)
                    );
                }

                db.Close();
            }

            if (IconPacks.Count == 0)
            {
                IconPack newIconPack = new IconPack();
                newIconPack.Author = Environment.UserName;
                newIconPack.Name = "New icon pack";
                newIconPack.Version = "1.0.0";
                newIconPack.Icons = new List<Icon>();
                IconPacks.Add(newIconPack);
                AddIconImage(newIconPack, Resources.Icon);
            }
        }

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
                foreach (IconPack iconPack in IconPacks)
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
                    Debug.WriteLine(iconPack.Name + " update: " + update.ToString());
                    if (update)
                    {
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
               db = new SQLiteConnection(MacroDeck.IconPackDirectoryPath + "\\" + iconPack.Name + ".db");
            } else
            {
                db = new SQLiteConnection(directory + "\\" + iconPack.Name + ".db");
            }
            db.CreateTable<IconPackJson>();
            db.DeleteAll<IconPackJson>();

            string jsonString = JsonConvert.SerializeObject(iconPack, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            });

            IconPackJson iconPackJson = new IconPackJson();
            iconPackJson.JsonString = jsonString;

            db.InsertOrReplace(iconPackJson);
            db.Close();
        }


        public static void CreateIconPack(string name, string author, string version)
        {
            IconPack iconPack = new IconPack();
            iconPack.Name = name.Replace(".", ""); ;
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
            IconPacks.Remove(iconPack);
            
            try
            {
                File.Delete(Path.Combine(MacroDeck.IconPackDirectoryPath, iconPack.Name, ".db"));
            }
            catch { }
            if (IconPacks.Count == 0)
            {
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

        public static Icon AddIconImage(IconPack iconPack, Image image)
        {
            try
            {
                String base64 = Utils.Base64.GetBase64FromImage(image);
                Icon icon = new Icon();
                icon.IconBase64 = base64;
                icon.IconId = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                icon.IconPack = iconPack.Name;
                iconPack.Icons.Add(icon);
                SaveIconPack(iconPack);
                MacroDeckServer.SendAllIcons();
                return icon;
            }
            catch { }
            return null;
        }

        public static void DeleteIcon(IconPack iconPack, Icon icon)
        {
            if (!iconPack.Icons.Contains(icon)) return;
            IconPacks[IconPacks.IndexOf(iconPack)].Icons.Remove(icon);
            
            SaveIconPack(iconPack);
            MacroDeckServer.SendAllIcons();
        }



    }
}
