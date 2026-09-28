using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonSharedVariableStore : ISharedVariableStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonSharedVariableStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_filePath = Path.Combine(paths.DataDirectory, "shared-variables.json");
		_logger = logger;
		_files = new DurableJsonFile("shared variable file",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public bool TryLoad(out IReadOnlyList<SharedVariable> entries)
	{
		lock (_lock)
		{
			var loaded = _files.Read<List<SharedVariable>>(_filePath);
			if (loaded is not null)
			{
				entries = loaded;
				return true;
			}

			if (!File.Exists(_filePath) &&
				!File.Exists(_filePath + DurableJsonFile.TempSuffix) &&
				!File.Exists(_filePath + DurableJsonFile.BackupSuffix))
			{
				entries = [];
				return true;
			}

			entries = [];
			return false;
		}
	}

	public bool Save(IEnumerable<SharedVariable> entries)
	{
		lock (_lock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, entries.ToList());
				return true;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write shared variables to {Path}", _filePath);
				return false;
			}
		}
	}
}
