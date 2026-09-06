using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Automations;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonAutomationStore : IAutomationStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly ConcurrentDictionary<Guid, object> _locks = new();
	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonAutomationStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_directory = paths.AutomationsDirectory;
		_logger = logger;
		_files = new DurableJsonFile("automation",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyList<AutomationFile> LoadAll()
	{
		var automations = new List<AutomationFile>();
		foreach (var path in DurableJsonFile.EnumerateDocumentPaths(_directory, ".json"))
		{
			AutomationFile? automation;
			if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id))
			{
				lock (LockFor(id))
				{
					automation = _files.Read<AutomationFile>(path);
				}
			}
			else
			{
				automation = _files.Read<AutomationFile>(path);
			}

			if (automation is not null)
			{
				automations.Add(automation);
			}
		}

		return automations;
	}

	public void Save(AutomationFile automation)
	{
		lock (LockFor(automation.Id))
		{
			try
			{
				Directory.CreateDirectory(_directory);
				_files.Write(PathFor(automation.Id), automation);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write automation {AutomationId}", automation.Id);
			}
		}
	}

	public void Delete(Guid id)
	{
		lock (LockFor(id))
		{
			try
			{
				_files.Delete(PathFor(id));
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to delete automation {AutomationId}", id);
			}
		}
	}

	private object LockFor(Guid id) => _locks.GetOrAdd(id, _ => new object());

	private string PathFor(Guid id) => Path.Combine(_directory, id + ".json");
}
