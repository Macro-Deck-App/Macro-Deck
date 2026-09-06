using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Scripts;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonScriptStore : IScriptStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly ConcurrentDictionary<Guid, object> _locks = new();
	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonScriptStore(IMacroDeckPaths paths, ILogger logger, IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_directory = paths.ScriptsDirectory;
		_logger = logger;
		_files = new DurableJsonFile("script", _options, logger, PersistenceBackup.KeepLastKnownGood, recoveryReporter);
	}

	public IReadOnlyList<ScriptFile> LoadAll()
	{
		var scripts = new List<ScriptFile>();
		foreach (var path in DurableJsonFile.EnumerateDocumentPaths(_directory, ".json"))
		{
			ScriptFile? script;
			if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id))
			{
				lock (LockFor(id))
				{
					script = _files.Read<ScriptFile>(path);
				}
			}
			else
			{
				script = _files.Read<ScriptFile>(path);
			}

			if (script is not null)
			{
				scripts.Add(script);
			}
		}

		return scripts;
	}

	public void Save(ScriptFile script)
	{
		lock (LockFor(script.Id))
		{
			try
			{
				Directory.CreateDirectory(_directory);
				_files.Write(PathFor(script.Id), script);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write script {ScriptId}", script.Id);
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
				_logger.Error(ex, "Failed to delete script {ScriptId}", id);
			}
		}
	}

	private object LockFor(Guid id) => _locks.GetOrAdd(id, _ => new object());

	private string PathFor(Guid id) => Path.Combine(_directory, id + ".json");
}
