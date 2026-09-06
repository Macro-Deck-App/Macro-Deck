using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class JsonStoreOperationStore : IStoreOperationStore
{
	private const string FileName = "operations.json";

	private readonly Lock _gate = new();
	private readonly string _path;
	private readonly DurableJsonFile _files;

	public JsonStoreOperationStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreDirectory, FileName);
		_files = new DurableJsonFile("store operations",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.None,
			recoveryReporter);
	}

	public IReadOnlyList<StoreOperation> LoadAll()
	{
		lock (_gate)
		{
			return _files.Read<List<StoreOperation>>(_path) ?? [];
		}
	}

	public void SaveAll(IReadOnlyList<StoreOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations);

		lock (_gate)
		{
			_files.Write(_path, operations);
		}
	}
}
