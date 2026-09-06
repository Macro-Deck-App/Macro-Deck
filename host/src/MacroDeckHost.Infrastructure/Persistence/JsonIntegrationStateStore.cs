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
			return _files.Read<Dictionary<string, bool>>(_filePath) ?? new Dictionary<string, bool>();
		}
	}

	public void Save(IReadOnlyDictionary<string, bool> states)
	{
		lock (_lock)
		{
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
}
