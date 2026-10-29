using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class JsonStoreTestInstallationStore : IStoreTestInstallationStore
{
	private const string FileName = "test-installations.json";

	private readonly Lock _gate = new();
	private readonly string _path;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonStoreTestInstallationStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreDirectory, FileName);
		_logger = logger;
		_files = new DurableJsonFile("store test installations",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public StoreTestInstallationRecord? Find(string pluginId)
	{
		lock (_gate)
		{
			return Read().FirstOrDefault(record => Same(record, pluginId));
		}
	}

	public void Save(StoreTestInstallationRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);

		lock (_gate)
		{
			if (ReadForWrite() is not { } records)
			{
				return;
			}

			records.RemoveAll(existing => Same(existing, record.PluginId));
			records.Add(record);
			_files.Write(_path, records);
		}
	}

	public void Delete(string pluginId)
	{
		lock (_gate)
		{
			if (ReadForWrite() is { } records && records.RemoveAll(record => Same(record, pluginId)) > 0)
			{
				_files.Write(_path, records);
			}
		}
	}

	private List<StoreTestInstallationRecord> Read()
	{
		return _files.Read<List<StoreTestInstallationRecord>>(_path) ?? [];
	}

	private List<StoreTestInstallationRecord>? ReadForWrite()
	{
		if (_files.Read<List<StoreTestInstallationRecord>>(_path) is { } records)
		{
			return records;
		}

		if (!File.Exists(_path) && !File.Exists(_path + DurableJsonFile.BackupSuffix))
		{
			return [];
		}

		_logger.Warning("The store test installation file {Path} could not be read; it is not saved until it is "
			+ "repaired",
			_path);
		return null;
	}

	private static bool Same(StoreTestInstallationRecord record, string pluginId)
	{
		return string.Equals(record.PluginId, pluginId, StringComparison.OrdinalIgnoreCase);
	}
}
