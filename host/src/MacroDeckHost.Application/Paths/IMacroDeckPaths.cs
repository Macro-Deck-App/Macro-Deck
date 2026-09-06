namespace MacroDeckHost.Application.Paths;

public interface IMacroDeckPaths
{
	string BaseDirectory { get; }
	string DataRootDirectory { get; }
	string ResourcesDirectory { get; }
	string ImagesDirectory { get; }
	string DataDirectory { get; }
	string ProfilesDirectory { get; }
	string ScriptsDirectory { get; }
	string AutomationsDirectory { get; }
	string IconsDirectory { get; }
	string IconPacksDirectory { get; }
	string IconStagingDirectory { get; }
	string DatabasePath { get; }
	string DatabaseMigrationsDirectory { get; }
	string ConfigDirectory { get; }
	string LogsDirectory { get; }
	string KeysDirectory { get; }
	string PluginsDirectory { get; }

	string PluginStagingDirectory { get; }

	string PluginCacheDirectory { get; }

	string BackupsDirectory { get; }

	string RestoreStagingDirectory { get; }

	/// <summary>
	/// Recreates every directory the host expects to find. Restore calls this after a swap, because an
	/// archive deliberately omits caches and staging directories that other components require to exist.
	/// </summary>
	void EnsureDirectoriesExist();

	string StoreDirectory { get; }

	string StoreRegistryDirectory { get; }

	string StoreRegistryCurrentDirectory { get; }

	string StoreRegistryStagingDirectory { get; }

	string StoreStagingDirectory { get; }

	string StoreMediaDirectory { get; }
}
