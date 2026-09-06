using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public interface IStoreRegistryStateStore
{
	StoreRegistryState? Load(string origin);

	void Save(StoreRegistryState state);
}

public sealed class JsonStoreRegistryStateStore : IStoreRegistryStateStore
{
	private const string FileName = "state.json";

	private readonly Lock _gate = new();
	private readonly string _path;
	private readonly DurableJsonFile _files;

	public JsonStoreRegistryStateStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreRegistryDirectory, FileName);
		_files = new DurableJsonFile("store registry state",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public StoreRegistryState? Load(string origin)
	{
		lock (_gate)
		{
			var states = _files.Read<List<StoreRegistryState>>(_path) ?? [];
			return states.FirstOrDefault(state =>
				string.Equals(state.Origin, origin, StringComparison.OrdinalIgnoreCase));
		}
	}

	public void Save(StoreRegistryState state)
	{
		ArgumentNullException.ThrowIfNull(state);

		lock (_gate)
		{
			var states = _files.Read<List<StoreRegistryState>>(_path) ?? [];
			states.RemoveAll(existing =>
				string.Equals(existing.Origin, state.Origin, StringComparison.OrdinalIgnoreCase));
			states.Add(state);
			_files.Write(_path, states);
		}
	}
}
