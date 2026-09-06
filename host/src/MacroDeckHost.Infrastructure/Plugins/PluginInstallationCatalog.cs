using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginInstallationCatalog : IPluginInstallationCatalog
{
	private readonly string _pluginsDirectory;
	private readonly object _lock = new();
	private readonly DurableJsonFile _currentVersionFiles;

	private CacheKey? _cacheKey;
	private IReadOnlyList<InstalledPlugin> _cached = [];

	public PluginInstallationCatalog(IMacroDeckPaths paths, ILogger logger)
	{
		_pluginsDirectory = paths.PluginsDirectory;
		_currentVersionFiles = new DurableJsonFile("plugin version pointer",
			PersistenceJsonOptions.Default,
			logger.ForContext<PluginInstallationCatalog>());
	}

	public IReadOnlyList<InstalledPlugin> Discover()
	{
		lock (_lock)
		{
			var key = ComputeCacheKey();
			if (_cacheKey is { } current && current.Equals(key))
			{
				return _cached;
			}

			_cached = DiscoverCore();
			_cacheKey = key;
			return _cached;
		}
	}

	public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
	{
		version = Discover().FirstOrDefault(p => string.Equals(p.PluginId, pluginId, StringComparison.Ordinal))
			?.ActiveVersion;
		return version is not null;
	}

	public void Invalidate()
	{
		lock (_lock)
		{
			_cacheKey = null;
			_cached = [];
		}
	}

	private CacheKey ComputeCacheKey()
	{
		if (!Directory.Exists(_pluginsDirectory))
		{
			return new CacheKey(false, DateTime.MinValue, 0);
		}

		try
		{
			var lastWrite = Directory.GetLastWriteTimeUtc(_pluginsDirectory);
			var entryCount = 0;

			foreach (var directory in Directory.EnumerateDirectories(_pluginsDirectory))
			{
				entryCount++;
				var written = Directory.GetLastWriteTimeUtc(directory);
				if (written > lastWrite)
				{
					lastWrite = written;
				}
			}

			return new CacheKey(true, lastWrite, entryCount);
		}
		catch (IOException)
		{
			return new CacheKey(false, DateTime.MinValue, 0);
		}
	}

	private List<InstalledPlugin> DiscoverCore()
	{
		var result = new List<InstalledPlugin>();

		if (!Directory.Exists(_pluginsDirectory))
		{
			return result;
		}

		foreach (var pluginDirectory in Directory.EnumerateDirectories(_pluginsDirectory))
		{
			var pluginId = Path.GetFileName(pluginDirectory);
			if (!PluginId.IsValid(pluginId))
			{
				continue;
			}

			var versionsDirectory = Path.Combine(pluginDirectory, "versions");
			var versions = new List<InstalledPluginVersion>();

			if (Directory.Exists(versionsDirectory))
			{
				foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory))
				{
					var version = Path.GetFileName(versionDirectory);
					var manifestPath = Path.Combine(versionDirectory, "manifest.json");
					if (!File.Exists(manifestPath))
					{
						continue;
					}

					versions.Add(new InstalledPluginVersion
					{
						Version = version,
						VersionDirectory = versionDirectory,
						ManifestPath = manifestPath
					});
				}
			}

			var activeVersion = TryReadActiveVersion(pluginDirectory, versions);

			result.Add(new InstalledPlugin
			{
				PluginId = pluginId,
				PluginDirectory = pluginDirectory,
				Versions = versions,
				ActiveVersion = activeVersion
			});
		}

		return result;
	}

	private InstalledPluginVersion? TryReadActiveVersion(string pluginDirectory,
		IReadOnlyList<InstalledPluginVersion> versions)
	{
		var currentPath = Path.Combine(pluginDirectory, "current.json");
		var current = _currentVersionFiles.Read<CurrentJson>(currentPath, repair: false);
		if (current?.Version is not { } activeVersion)
		{
			return null;
		}

		return versions.FirstOrDefault(v => string.Equals(v.Version, activeVersion, StringComparison.Ordinal));
	}

	private sealed record CurrentJson
	{
		public string? Version { get; init; }
	}

	private readonly record struct CacheKey(bool Exists, DateTime LastWriteUtc, int EntryCount);
}
