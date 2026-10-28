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
	private bool _checked;
	private bool _persistable = true;

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
			return Read();
		}
	}

	public void Save(IEnumerable<VariableEntity> userVariables)
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
				_files.Write(_filePath,
					userVariables
						.Select(variable => variable.FileSource is null && variable.TemplateSource is null
							? variable
							: variable.CopyWithValue(string.Empty))
						.ToList());
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write user variables to {Path}", _filePath);
			}
		}
	}

	private List<VariableEntity> Read()
	{
		_checked = true;
		if (_files.Read<List<VariableEntity>>(_filePath) is { } loaded)
		{
			return loaded;
		}

		// Read returns null for a missing file and an unrecoverable one alike; overwriting the latter would rotate
		// the unreadable copies away. A lone .tmp is never committed data, so it does not block.
		if (_persistable && (File.Exists(_filePath) || File.Exists(_filePath + DurableJsonFile.BackupSuffix)))
		{
			_persistable = false;
			_logger.Warning("The user variable file {Path} could not be read; user variables are not saved until it "
				+ "is repaired",
				_filePath);
		}

		return [];
	}
}
