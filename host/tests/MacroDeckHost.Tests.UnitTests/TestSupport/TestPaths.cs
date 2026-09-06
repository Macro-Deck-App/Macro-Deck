using MacroDeckHost.Application.Paths;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class TestPaths : IMacroDeckPaths
{
	public string BaseDirectory { get; } =
		Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));

	public string DataRootDirectory => BaseDirectory;
	public string ResourcesDirectory => Path.Combine(BaseDirectory, "resources");
	public string ImagesDirectory => Path.Combine(ResourcesDirectory, "images");
	public string DataDirectory => Path.Combine(BaseDirectory, "data");
	public string ProfilesDirectory => Path.Combine(DataDirectory, "profiles");
	public string ScriptsDirectory => Path.Combine(DataDirectory, "scripts");
	public string AutomationsDirectory => Path.Combine(DataDirectory, "automations");
	public string IconsDirectory => Path.Combine(DataDirectory, "icons");
	public string IconPacksDirectory => Path.Combine(IconsDirectory, "packs");
	public string IconStagingDirectory => Path.Combine(IconsDirectory, "staging");
	public string DatabasePath => Path.Combine(BaseDirectory, "database.db");
	public string DatabaseMigrationsDirectory => Path.Combine(BaseDirectory, "DatabaseMigrations");
	public string ConfigDirectory => Path.Combine(BaseDirectory, "config");
	public string LogsDirectory => Path.Combine(BaseDirectory, "logs");
	public string KeysDirectory => Path.Combine(BaseDirectory, "keys");
	public string PluginsDirectory => Path.Combine(BaseDirectory, "plugins");
	public string PluginStagingDirectory => Path.Combine(PluginsDirectory, "_staging");
	public string PluginCacheDirectory => Path.Combine(PluginsDirectory, "_cache");
	public string BackupsDirectory => Path.Combine(BaseDirectory, "backups");
	public string RestoreStagingDirectory => Path.Combine(BackupsDirectory, "restore-staging");

	public void EnsureDirectoriesExist()
	{
		foreach (var directory in new[]
			{
				ResourcesDirectory, ImagesDirectory, DataDirectory, ProfilesDirectory, ScriptsDirectory,
				AutomationsDirectory, IconsDirectory, IconPacksDirectory, IconStagingDirectory, ConfigDirectory,
				LogsDirectory, KeysDirectory, PluginsDirectory, PluginStagingDirectory, PluginCacheDirectory,
				BackupsDirectory, RestoreStagingDirectory
			})
		{
			Directory.CreateDirectory(directory);
		}
	}

	public string StoreDirectory => Path.Combine(BaseDirectory, "store");
	public string StoreRegistryDirectory => Path.Combine(StoreDirectory, "registry");
	public string StoreRegistryCurrentDirectory => Path.Combine(StoreRegistryDirectory, "current");
	public string StoreRegistryStagingDirectory => Path.Combine(StoreRegistryDirectory, "staging");
	public string StoreStagingDirectory => Path.Combine(StoreDirectory, "staging");
	public string StoreMediaDirectory => Path.Combine(StoreDirectory, "media");

	public void Cleanup()
	{
		if (Directory.Exists(BaseDirectory))
		{
			Directory.Delete(BaseDirectory, recursive: true);
		}
	}
}
