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
	private readonly DurableJsonFile _files;

	public JsonStoreTestInstallationStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreDirectory, FileName);
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
			var records = Read();
			records.RemoveAll(existing => Same(existing, record.PluginId));
			records.Add(record);
			_files.Write(_path, records);
		}
	}

	public void Delete(string pluginId)
	{
		lock (_gate)
		{
			var records = Read();
			if (records.RemoveAll(record => Same(record, pluginId)) > 0)
			{
				_files.Write(_path, records);
			}
		}
	}

	private List<StoreTestInstallationRecord> Read() =>
		_files.Read<List<StoreTestInstallationRecord>>(_path) ?? [];

	private static bool Same(StoreTestInstallationRecord record, string pluginId) =>
		string.Equals(record.PluginId, pluginId, StringComparison.OrdinalIgnoreCase);
}
