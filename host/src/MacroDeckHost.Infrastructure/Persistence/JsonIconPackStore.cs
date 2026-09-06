using System.Collections.Concurrent;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Icons;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonIconPackStore : IIconPackStore
{
	private const string ManifestFileName = "pack.json";

	private readonly ConcurrentDictionary<Guid, object> _locks = new();
	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonIconPackStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_directory = paths.IconPacksDirectory;
		_logger = logger;
		_files = new DurableJsonFile("icon pack",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.None,
			recoveryReporter);
	}

	public IReadOnlyList<IconPackManifest> LoadAll()
	{
		if (!Directory.Exists(_directory))
		{
			return [];
		}

		var manifests = new List<IconPackManifest>();
		foreach (var packDirectory in Directory.EnumerateDirectories(_directory))
		{
			var path = Path.Combine(packDirectory, ManifestFileName);
			var packId = Path.GetFileName(packDirectory);

			IconPackManifest? manifest;
			if (Guid.TryParse(packId, out var id))
			{
				lock (LockFor(id))
				{
					manifest = _files.Read<IconPackManifest>(path);
				}
			}
			else
			{
				manifest = _files.Read<IconPackManifest>(path);
			}

			if (manifest is null)
			{
				_logger.Warning("Icon pack directory {Directory} has no readable {Manifest}; skipping",
					packDirectory,
					ManifestFileName);
				continue;
			}

			manifests.Add(manifest);
		}

		return manifests;
	}

	public void Save(IconPackManifest manifest)
	{
		lock (LockFor(manifest.Id))
		{
			try
			{
				var packDirectory = PackDirectoryFor(manifest.Id);
				Directory.CreateDirectory(packDirectory);
				_files.Write(Path.Combine(packDirectory, ManifestFileName), manifest);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write icon pack manifest {PackId}", manifest.Id);
			}
		}
	}

	public void Delete(Guid packId)
	{
		lock (LockFor(packId))
		{
			try
			{
				var packDirectory = PackDirectoryFor(packId);
				if (Directory.Exists(packDirectory))
				{
					Directory.Delete(packDirectory, recursive: true);
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to delete icon pack folder {PackId}", packId);
			}
		}
	}

	private object LockFor(Guid id) => _locks.GetOrAdd(id, _ => new object());

	private string PackDirectoryFor(Guid id) => Path.Combine(_directory, id.ToString());
}
