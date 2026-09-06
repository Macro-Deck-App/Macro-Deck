using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeckHost.Application.Paths;

public class MacroDeckPaths : IMacroDeckPaths
{
	public string BaseDirectory { get; }

	public string DataRootDirectory { get; }

	public string ResourcesDirectory => Path.Combine(DataRootDirectory, "resources");

	public string ImagesDirectory => Path.Combine(ResourcesDirectory, "images");

	public string DataDirectory => Path.Combine(DataRootDirectory, "data");

	public string ProfilesDirectory => Path.Combine(DataDirectory, "profiles");

	public string ScriptsDirectory => Path.Combine(DataDirectory, "scripts");

	public string AutomationsDirectory => Path.Combine(DataDirectory, "automations");

	public string IconsDirectory => Path.Combine(DataDirectory, "icons");

	public string IconPacksDirectory => Path.Combine(IconsDirectory, "packs");

	public string IconStagingDirectory => Path.Combine(IconsDirectory, "staging");

	public string DatabasePath => Path.Combine(DataRootDirectory, "database.db");

	public string DatabaseMigrationsDirectory => Path.Combine(BaseDirectory, "DatabaseMigrations");

	public string ConfigDirectory => Path.Combine(DataRootDirectory, "config");

	public string LogsDirectory => Path.Combine(DataRootDirectory, "logs");

	public string KeysDirectory => Path.Combine(DataRootDirectory, "keys");

	public string PluginsDirectory => Path.Combine(DataRootDirectory, "plugins");

	public string PluginStagingDirectory => Path.Combine(PluginsDirectory, PluginArtifactFiles.StagingDirectoryName);

	public string PluginCacheDirectory => Path.Combine(PluginsDirectory, PluginArtifactFiles.CacheDirectoryName);

	public string BackupsDirectory => Path.Combine(DataRootDirectory, "backups");

	public string RestoreStagingDirectory => Path.Combine(BackupsDirectory, "restore-staging");
	public string StoreDirectory => Path.Combine(DataRootDirectory, "store");

	public string StoreRegistryDirectory => Path.Combine(StoreDirectory, "registry");

	public string StoreRegistryCurrentDirectory => Path.Combine(StoreRegistryDirectory, "current");

	public string StoreRegistryStagingDirectory => Path.Combine(StoreRegistryDirectory, "staging");

	public string StoreStagingDirectory => Path.Combine(StoreDirectory, "staging");

	public string StoreMediaDirectory => Path.Combine(StoreDirectory, "media");

	public MacroDeckPaths()
		: this(DataRootEnvironment.Current())
	{
	}

	public MacroDeckPaths(DataRootEnvironment environment)
	{
		BaseDirectory = environment.BaseDirectory;
		DataRootDirectory = MacroDeckDataRootResolver.Resolve(environment);
		EnsureDirectoriesExist();
	}

	public void EnsureDirectoriesExist()
	{
		Directory.CreateDirectory(ResourcesDirectory);
		Directory.CreateDirectory(ImagesDirectory);
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(ProfilesDirectory);
		Directory.CreateDirectory(ScriptsDirectory);
		Directory.CreateDirectory(AutomationsDirectory);
		Directory.CreateDirectory(IconsDirectory);
		Directory.CreateDirectory(IconPacksDirectory);
		Directory.CreateDirectory(IconStagingDirectory);
		Directory.CreateDirectory(ConfigDirectory);
		Directory.CreateDirectory(LogsDirectory);
		Directory.CreateDirectory(KeysDirectory);
		Directory.CreateDirectory(PluginsDirectory);
		Directory.CreateDirectory(PluginStagingDirectory);
		Directory.CreateDirectory(PluginCacheDirectory);
		Directory.CreateDirectory(BackupsDirectory);
		Directory.CreateDirectory(RestoreStagingDirectory);
		Directory.CreateDirectory(StoreDirectory);
		Directory.CreateDirectory(StoreRegistryDirectory);
		Directory.CreateDirectory(StoreRegistryStagingDirectory);
		Directory.CreateDirectory(StoreStagingDirectory);
		Directory.CreateDirectory(StoreMediaDirectory);
	}
}
