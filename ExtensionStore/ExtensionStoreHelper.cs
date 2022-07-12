using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowViews;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.ExtensionStore
{
    public class ExtensionStoreHelper
    {
        public static event EventHandler OnUpdateCheckFinished;

        private static ExtensionStoreDownloader extensionStoreDownloader;

        public static event EventHandler OnInstallationFinished;

        private static string _updateNotification;

        private static bool _updateCheckRunning = false;

        public static void InstallPluginById(string packageId)
        {
            InstallPackages(new List<ExtensionStoreDownloaderPackageInfoModel> { new ExtensionStoreDownloaderPackageInfoModel() { PackageId = packageId, ExtensionType = ExtensionType.Plugin } });
        }

        public static void InstallIconPackById(string packageId)
        {
            InstallPackages(new List<ExtensionStoreDownloaderPackageInfoModel> { new ExtensionStoreDownloaderPackageInfoModel() { PackageId = packageId, ExtensionType = ExtensionType.IconPack } });
        }

        internal static void InstallById(string packageId)
        {
            InstallPackages(new List<ExtensionStoreDownloaderPackageInfoModel> { new ExtensionStoreDownloaderPackageInfoModel() { PackageId = packageId } });
        }

        public static void InstallPackages(List<ExtensionStoreDownloaderPackageInfoModel> packages)
        {
            if (MacroDeck.MainWindow == null) return;
            MacroDeck.MainWindow.Invoke(new Action(() =>
            {
                extensionStoreDownloader = new ExtensionStoreDownloader(packages)
                {
                    Owner = MacroDeck.MainWindow,
                };
                extensionStoreDownloader.ShowDialog();
                if (OnInstallationFinished != null)
                {
                    OnInstallationFinished(null, EventArgs.Empty);
                }
            }));
        }

        public static string GetPackageId(MacroDeckPlugin macroDeckPlugin)
        {
            return new DirectoryInfo(PluginManager.PluginDirectories[macroDeckPlugin]).Name;
        }

        public static void SearchUpdatesAsync()
        {
            if (_updateCheckRunning) return;
            _updateCheckRunning = true;
            PluginManager.PluginsUpdateAvailable.Clear();
            IconManager.IconPacksUpdateAvailable.Clear();
            Task.Run(() =>
            {
                foreach (MacroDeckPlugin plugin in PluginManager.Plugins.Values)
                {
                    PluginManager.SearchUpdate(plugin);
                }
                foreach (MacroDeckPlugin plugin in PluginManager.PluginsNotLoaded.Values)
                {
                    PluginManager.SearchUpdate(plugin);
                }
                foreach (IconPack iconPack in IconManager.IconPacks.FindAll(iP => iP.ExtensionStoreManaged && !iP.Hidden))
                {
                    IconManager.SearchUpdate(iconPack);
                }

                if (NotificationManager.GetNotification(_updateNotification) == null && (PluginManager.PluginsUpdateAvailable.Count + IconManager.IconPacksUpdateAvailable.Count) > 0)
                {
                    var btnOpenExtensionManager = new ButtonPrimary()
                    {
                        AutoSize = true,
                        Text = "Open Extension Manager"
                    };
                    btnOpenExtensionManager.Click += (sender, e) =>
                    {
                        MacroDeck.MainWindow?.SetView(new ExtensionsView());
                    };
                    var btnUpdateAll = new ButtonPrimary()
                    {
                        AutoSize = true,
                        Text = LanguageManager.Strings.UpdateAll,
                    };
                    btnUpdateAll.Click += (sender, e) =>
                    {
                        NotificationManager.RemoveNotification(NotificationManager.GetNotification(_updateNotification));
                        UpdateAllPackages();
                    };
                    _updateNotification = NotificationManager.SystemNotification("Extension Store", "Updates available", true, image: Properties.Resources.Macro_Deck_2021_update, controls: new List<System.Windows.Forms.Control>() { btnOpenExtensionManager, btnUpdateAll });
                }

                _updateCheckRunning = false;

                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
                }
            });
        }

        public static void UpdateAllPackages()
        {
            List<ExtensionStoreDownloaderPackageInfoModel> packages = new List<ExtensionStoreDownloaderPackageInfoModel>();
            foreach (var updatePlugin in PluginManager.PluginsUpdateAvailable)
            {
                if (PluginManager.UpdatedPlugins.Contains(updatePlugin)) continue;
                packages.Add(new ExtensionStoreDownloaderPackageInfoModel()
                {
                    ExtensionType = ExtensionType.Plugin,
                    PackageId = ExtensionStoreHelper.GetPackageId(updatePlugin)
                });
            }
            foreach (var updateIconPack in IconManager.IconPacksUpdateAvailable)
            {
                packages.Add(new ExtensionStoreDownloaderPackageInfoModel()
                {
                    ExtensionType = ExtensionType.IconPack,
                    PackageId = updateIconPack.PackageId,
                });
            }
            InstallPackages(packages);
        }

        public static string InstalledIconPacksAsString
        {
            get
            {
                string installedPlugins = "";
                foreach (var iconPack in IconManager.IconPacks.FindAll(x => x.ExtensionStoreManaged))
                {
                    installedPlugins += $"{iconPack.PackageId.ToLower()}%20";
                }

                return installedPlugins;
            }
        }

        public static string InstalledPluginsAsString
        {
            get
            {
                string installedPlugins = "";
                foreach (var path in PluginManager.PluginDirectories.Values)
                {
                    installedPlugins += $"{new DirectoryInfo(path).Name.ToLower()}%20";
                }

                return installedPlugins;
            }
        }
    }

    public enum ExtensionType
    {
        Plugin,
        IconPack,
    }
}
