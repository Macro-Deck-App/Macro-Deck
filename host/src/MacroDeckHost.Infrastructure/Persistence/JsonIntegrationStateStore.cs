using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonIntegrationStateStore : IIntegrationStateStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;
	private bool _checked;
	private bool _persistable = true;

	public JsonIntegrationStateStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_filePath = Path.Combine(paths.DataDirectory, "integration-states.json");
		_logger = logger;
		_files = new DurableJsonFile("integration state file",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyDictionary<string, bool> Load()
	{
		lock (_lock)
		{
			return Read();
		}
	}

	public void Save(IReadOnlyDictionary<string, bool> states)
	{
		lock (_lock)
		{
			if (!_checked)
			{
				Read();
			}

			if (!_persistable)
			{
				return;
			}

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, states);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write integration states to {Path}", _filePath);
			}
		}
	}

	private Dictionary<string, bool> Read()
	{
		_checked = true;
		if (_files.Read<Dictionary<string, bool>>(_filePath) is { } loaded)
		{
			return loaded;
		}

		// Read returns null for a missing file and an unrecoverable one alike; overwriting the latter would rotate
		// the unreadable copies away. A lone .tmp is never committed data, so it does not block.
		if (_persistable && (File.Exists(_filePath) || File.Exists(_filePath + DurableJsonFile.BackupSuffix)))
		{
			_persistable = false;
			_logger.Warning("The integration state file {Path} could not be read; integration states are not saved "
				+ "until it is repaired",
				_filePath);
		}

		return new Dictionary<string, bool>();
	}
}
