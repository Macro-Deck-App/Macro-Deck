using System.Collections.Concurrent;
using MacroDeck.Sdk.Storage;
using MacroDeckHost.Application.Paths;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class IntegrationStorageProvider : IDisposable
{
	public const string FolderName = "integration-data";

	// Keyed by storage key alone, because naming the integration here would couple the host to it (ADR 0107).
	// A file listed here is adopted whenever it exists, so restoring an archive that predates storage wins.
	private static readonly Dictionary<string, string> _legacyFiles =
		new(StringComparer.Ordinal)
		{
			["system-audio-devices.json"] = "system-audio-devices.json"
		};

	private static readonly string[] _copySuffixes =
		["", DirectoryIntegrationStorage.TempSuffix, DirectoryIntegrationStorage.BackupSuffix];

	private readonly ConcurrentDictionary<string, DirectoryIntegrationStorage> _storages = new(StringComparer.Ordinal);
	private readonly string _dataDirectory;
	private readonly ILogger _logger;

	public IntegrationStorageProvider(IMacroDeckPaths paths, ILogger logger)
	{
		_dataDirectory = paths.DataDirectory;
		_logger = logger.ForContext<IntegrationStorageProvider>();
	}

	public IIntegrationStorage For(string integrationId)
	{
		if (!IsSafeDirectoryName(integrationId))
		{
			return UnavailableIntegrationStorage.Instance;
		}

		return _storages.GetOrAdd(integrationId,
			id => new DirectoryIntegrationStorage(Path.Combine(_dataDirectory, FolderName, id), AdoptLegacy));
	}

	public void Dispose()
	{
		foreach (var storage in _storages.Values)
		{
			storage.Dispose();
		}
	}

	private void AdoptLegacy(string key, string path)
	{
		if (!_legacyFiles.TryGetValue(key, out var legacyName))
		{
			return;
		}

		var legacyPath = Path.Combine(_dataDirectory, legacyName);
		if (!_copySuffixes.Any(suffix => File.Exists(legacyPath + suffix)))
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			foreach (var suffix in _copySuffixes)
			{
				File.Delete(path + suffix);
			}

			foreach (var suffix in _copySuffixes)
			{
				if (File.Exists(legacyPath + suffix))
				{
					File.Move(legacyPath + suffix, path + suffix, true);
				}
			}

			_logger.Information("Moved {LegacyPath} into integration storage at {Path}", legacyPath, path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(exception, "Could not move {LegacyPath} into integration storage", legacyPath);
		}
	}

	private static bool IsSafeDirectoryName(string? name)
	{
		return !string.IsNullOrWhiteSpace(name)
			&& name is not ("." or "..")
			&& name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
			&& name.IndexOfAny(['/', '\\', ':']) < 0;
	}
}
