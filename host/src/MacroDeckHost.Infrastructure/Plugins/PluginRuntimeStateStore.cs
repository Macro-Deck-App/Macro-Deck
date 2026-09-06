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
			var states = new Dictionary<string, bool>(Load());
			states[pluginId] = started;
			Write(states);
		}

		return Task.CompletedTask;
	}

	public Task Remove(string pluginId)
	{
		lock (_lock)
		{
			var states = new Dictionary<string, bool>(Load());
			if (states.Remove(pluginId))
			{
				Write(states);
			}
		}

		return Task.CompletedTask;
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
