using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class JsonStoreInstallationStore : IStoreInstallationStore
{
	private const string FileName = "installations.json";

	private readonly Lock _gate = new();
	private readonly string _path;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonStoreInstallationStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreDirectory, FileName);
		_logger = logger;
		_files = new DurableJsonFile("store installations",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyList<StoreInstallationRecord> LoadAll()
	{
		lock (_gate)
		{
			return Read();
		}
	}

	public StoreInstallationRecord? Find(StoreExtensionKind kind, string packageId)
	{
		lock (_gate)
		{
			return Read().FirstOrDefault(record => Same(record, kind, packageId));
		}
	}

	public void Save(StoreInstallationRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);

		lock (_gate)
		{
			if (ReadForWrite() is not { } records)
			{
				return;
			}

			var previous = records.FirstOrDefault(existing => Same(existing, record.Kind, record.PackageId));
			records.RemoveAll(existing => Same(existing, record.Kind, record.PackageId));
			// An update keeps the first install time, so it cannot restart the rating prompt wait.
			records.Add(previous is not null
				&& string.Equals(previous.Origin, record.Origin, StringComparison.OrdinalIgnoreCase)
					? record with { InstalledAt = previous.InstalledAt }
					: record);
			_files.Write(_path, records);
		}
	}

	public void Delete(StoreExtensionKind kind, string packageId)
	{
		lock (_gate)
		{
			if (ReadForWrite() is { } records && records.RemoveAll(record => Same(record, kind, packageId)) > 0)
			{
				_files.Write(_path, records);
			}
		}
	}

	private List<StoreInstallationRecord> Read()
	{
		return _files.Read<List<StoreInstallationRecord>>(_path) ?? [];
	}

	private List<StoreInstallationRecord>? ReadForWrite()
	{
		if (_files.Read<List<StoreInstallationRecord>>(_path) is { } records)
		{
			return records;
		}

		if (!File.Exists(_path) && !File.Exists(_path + DurableJsonFile.BackupSuffix))
		{
			return [];
		}

		_logger.Warning("The store installation file {Path} could not be read; it is not saved until it is repaired",
			_path);
		return null;
	}

	private static bool Same(StoreInstallationRecord record, StoreExtensionKind kind, string packageId)
	{
		return record.Kind == kind && string.Equals(record.PackageId, packageId, StringComparison.OrdinalIgnoreCase);
	}
}
