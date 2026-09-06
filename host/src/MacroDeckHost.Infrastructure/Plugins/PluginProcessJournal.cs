using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginProcessJournal : IPluginProcessJournal
{
	private const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public PluginProcessJournal(IMacroDeckPaths paths, ILogger logger)
	{
		_filePath = Path.Combine(paths.ConfigDirectory, "plugin-processes.json");
		_logger = logger;
		_files = new DurableJsonFile("plugin process journal", _options, logger);
	}

	public PluginProcessJournalSnapshot Load()
	{
		lock (_lock)
		{
			return ToSnapshot(Read());
		}
	}

	public Task Record(PluginProcessJournalEntry entry)
	{
		lock (_lock)
		{
			var document = Read() ?? new JournalDocument();
			document.Entries[entry.LaunchId] = new JournalEntry
			{
				PluginId = entry.PluginId,
				ProcessId = entry.ProcessId,
				StartedAt = entry.StartedAt
			};

			Write(document);
		}

		return Task.CompletedTask;
	}

	public Task Remove(string launchId) => Remove([launchId]);

	public Task Remove(IReadOnlyCollection<string> launchIds)
	{
		if (launchIds.Count == 0)
		{
			return Task.CompletedTask;
		}

		lock (_lock)
		{
			var document = Read();
			if (document is null)
			{
				return Task.CompletedTask;
			}

			var changed = false;
			foreach (var launchId in launchIds)
			{
				changed |= document.Entries.Remove(launchId);
			}

			if (changed)
			{
				Write(document);
			}
		}

		return Task.CompletedTask;
	}

	private JournalDocument? Read() => _files.Read<JournalDocument>(_filePath);

	private void Write(JournalDocument document)
	{
		document.Version = CurrentVersion;
		document.OwnerProcessId = HostProcessIdentity.ProcessId;
		document.OwnerStartedAt = HostProcessIdentity.StartedAt;

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
			_files.Write(_filePath, document);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			_logger.Error(ex, "Failed to write the plugin process journal to {Path}", _filePath);
		}
	}

	private static PluginProcessJournalSnapshot ToSnapshot(JournalDocument? document)
	{
		if (document is null)
		{
			return new PluginProcessJournalSnapshot();
		}

		return new PluginProcessJournalSnapshot
		{
			Owner = document.OwnerProcessId > 0
				? new PluginProcessJournalOwner
				{
					ProcessId = document.OwnerProcessId,
					StartedAt = document.OwnerStartedAt
				}
				: null,
			Entries = document.Entries
				.Where(pair => pair.Value is not null)
				.Select(pair => new PluginProcessJournalEntry
				{
					LaunchId = pair.Key,
					PluginId = pair.Value.PluginId,
					ProcessId = pair.Value.ProcessId,
					StartedAt = pair.Value.StartedAt
				})
				.ToList()
		};
	}

	private sealed class JournalDocument
	{
		public int Version { get; set; } = CurrentVersion;

		public int OwnerProcessId { get; set; }

		public DateTimeOffset OwnerStartedAt { get; set; }

		public Dictionary<string, JournalEntry> Entries { get; set; } = new(StringComparer.Ordinal);
	}

	private sealed class JournalEntry
	{
		public string PluginId { get; set; } = string.Empty;

		public int ProcessId { get; set; }

		public DateTimeOffset StartedAt { get; set; }
	}
}
