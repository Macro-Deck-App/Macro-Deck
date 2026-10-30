using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Mcp;

// Lives outside every backup group: undo history and grants describe this installation's live state only.
public sealed class McpStateStore : IMcpStateStore
{
	public const string DirectoryName = "mcp";

	private readonly string _directory;
	private readonly DurableJsonFile _file;
	private readonly Lock _gate = new();

	public McpStateStore(IMacroDeckPaths paths, ILogger logger)
	{
		_directory = Path.Combine(paths.DataDirectory, DirectoryName);
		_file = new DurableJsonFile("MCP state",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood);
	}

	public T? Read<T>(string document)
		where T : class
	{
		lock (_gate)
		{
			var path = PathOf(document);
			return File.Exists(path) ? _file.Read<T>(path) : null;
		}
	}

	public void Write<T>(string document, T value)
		where T : class
	{
		lock (_gate)
		{
			Directory.CreateDirectory(_directory);
			_file.Write(PathOf(document), value);
		}
	}

	public void Delete(string document)
	{
		lock (_gate)
		{
			_file.Delete(PathOf(document));
		}
	}

	public static void DeleteJournal(IMacroDeckPaths paths)
	{
		var path = Path.Combine(paths.DataDirectory, DirectoryName, IMcpStateStore.JournalDocument + ".json");
		foreach (var candidate in new[] { path, path + DurableJsonFile.BackupSuffix })
		{
			if (File.Exists(candidate))
			{
				File.Delete(candidate);
			}
		}
	}

	private string PathOf(string document)
	{
		return Path.Combine(_directory, document + ".json");
	}
}
