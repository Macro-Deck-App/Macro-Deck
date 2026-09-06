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
	private readonly DurableJsonFile _files;

	public JsonStoreInstallationStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.StoreDirectory, FileName);
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
			var records = Read();
			records.RemoveAll(existing => Same(existing, record.Kind, record.PackageId));
			records.Add(record);
			_files.Write(_path, records);
		}
	}

	public void Delete(StoreExtensionKind kind, string packageId)
	{
		lock (_gate)
		{
			var records = Read();
			if (records.RemoveAll(record => Same(record, kind, packageId)) > 0)
			{
				_files.Write(_path, records);
			}
		}
	}

	private List<StoreInstallationRecord> Read() =>
		_files.Read<List<StoreInstallationRecord>>(_path) ?? [];

	private static bool Same(StoreInstallationRecord record, StoreExtensionKind kind, string packageId) =>
		record.Kind == kind && string.Equals(record.PackageId, packageId, StringComparison.OrdinalIgnoreCase);
}
