using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonVariableBindingStore : IVariableBindingStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	// Reread on every call is what the catalog update loop's 250ms poll tick was doing before this cache
	// existed - four disk reads a second, forever, for data that changes only on a bind/unbind/rename.
	// Invalidated by Save and by a failed Load, so it can never serve data staler than the last successful
	// read from this process's point of view.
	private List<VariableBinding>? _cache;

	public JsonVariableBindingStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		// The file name and every field inside it predate the rename of these types and stay as they are:
		// an installed Macro Deck must keep loading the bindings it already wrote.
		_filePath = Path.Combine(paths.DataDirectory, "dynamic-variable-bindings.json");
		_logger = logger;
		_files = new DurableJsonFile("variable binding file",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyList<VariableBinding> Load()
	{
		TryLoad(out var bindings);
		return bindings;
	}

	public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
	{
		lock (_lock)
		{
			if (_cache is { } cached)
			{
				bindings = cached;
				return true;
			}

			var loaded = _files.Read<List<VariableBinding>>(_filePath);
			if (loaded is not null)
			{
				_cache = loaded;
				bindings = loaded;
				return true;
			}

			// DurableJsonFile.Read returns null both when there is genuinely nothing on disk yet and when
			// a file is present but unrecoverable (it already logged the latter). Only the absence of the
			// primary file and every recovery candidate is safe to treat as "no bindings" - anything else
			// must be reported as a failed read so a load-modify-save caller does not overwrite it.
			if (!File.Exists(_filePath) &&
				!File.Exists(_filePath + DurableJsonFile.TempSuffix) &&
				!File.Exists(_filePath + DurableJsonFile.BackupSuffix))
			{
				_cache = [];
				bindings = _cache;
				return true;
			}

			bindings = [];
			return false;
		}
	}

	public bool Save(IEnumerable<VariableBinding> bindings)
	{
		lock (_lock)
		{
			var list = bindings.ToList();
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, list);
				_cache = list;
				return true;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write variable bindings to {Path}", _filePath);

				// The write failed, so the cache must not go on serving what the caller believed had
				// already been persisted; forcing the next read back to disk is what lets a later,
				// successful read recover instead of repeating this stale in-memory state forever.
				_cache = null;
				return false;
			}
		}
	}
}
