using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonUserVariableStore : IUserVariableStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonUserVariableStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_filePath = Path.Combine(paths.DataDirectory, "user-variables.json");
		_logger = logger;
		_files = new DurableJsonFile("user variable file",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyList<VariableEntity> Load()
	{
		lock (_lock)
		{
			return _files.Read<List<VariableEntity>>(_filePath) ?? [];
		}
	}

	public void Save(IEnumerable<VariableEntity> userVariables)
	{
		lock (_lock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, userVariables.ToList());
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write user variables to {Path}", _filePath);
			}
		}
	}
}
