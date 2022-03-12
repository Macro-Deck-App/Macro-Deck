using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Model;
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
            extensionStoreDownloader = new ExtensionStoreDownloader(packages)
            {
                Owner = MacroDeck.MainWindow,
            };
            extensionStoreDownloader.Show();
        }

        public static string GetPackageId(MacroDeckPlugin macroDeckPlugin)
        {
            return new DirectoryInfo(PluginManager.PluginDirectories[macroDeckPlugin]).Name;
        }

        public static void SearchUpdatesAsync()
        {
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

                if (OnUpdateCheckFinished != null)
                {
                    OnUpdateCheckFinished(null, EventArgs.Empty);
                }
            });
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
