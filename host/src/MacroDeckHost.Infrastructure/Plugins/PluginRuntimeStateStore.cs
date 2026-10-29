using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginRuntimeStateStore : IPluginRuntimeStateStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public PluginRuntimeStateStore(IMacroDeckPaths paths, ILogger logger)
	{
		_filePath = Path.Combine(paths.ConfigDirectory, "plugin-runtime.json");
		_logger = logger;
		_files = new DurableJsonFile("plugin runtime state file", _options, logger);
	}

	public IReadOnlyDictionary<string, bool> Load()
	{
		lock (_lock)
		{
			return _files.Read<Dictionary<string, bool>>(_filePath) ?? new Dictionary<string, bool>();
		}
	}

	public Task Save(string pluginId, bool started)
	{
		lock (_lock)
		{
			if (ReadForWrite() is not { } states)
			{
				return Task.CompletedTask;
			}

			states[pluginId] = started;
			Write(states);
		}

		return Task.CompletedTask;
	}

	public Task Remove(string pluginId)
	{
		lock (_lock)
		{
			if (ReadForWrite() is { } states && states.Remove(pluginId))
			{
				Write(states);
			}
		}

		return Task.CompletedTask;
	}

	private Dictionary<string, bool>? ReadForWrite()
	{
		if (_files.Read<Dictionary<string, bool>>(_filePath) is { } states)
		{
			return states;
		}

		if (!File.Exists(_filePath) && !File.Exists(_filePath + DurableJsonFile.BackupSuffix))
		{
			return new Dictionary<string, bool>();
		}

		_logger.Warning("The plugin runtime state file {Path} could not be read; it is not saved until it is "
			+ "repaired",
			_filePath);
		return null;
	}

	private void Write(Dictionary<string, bool> states)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
			_files.Write(_filePath, states);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			_logger.Error(ex, "Failed to write plugin runtime state to {Path}", _filePath);
		}
	}
}
