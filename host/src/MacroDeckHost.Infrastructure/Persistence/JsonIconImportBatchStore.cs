using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Icons;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonIconImportBatchStore : IIconImportBatchStore
{
	private const string BatchFileName = "batch.json";

	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly ConcurrentDictionary<Guid, object> _locks = new();
	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonIconImportBatchStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_directory = paths.IconStagingDirectory;
		_logger = logger;
		_files = new DurableJsonFile("icon import batch", _options, logger, PersistenceBackup.None, recoveryReporter);
	}

	public IReadOnlyList<IconImportBatchFile> LoadAll()
	{
		if (!Directory.Exists(_directory))
		{
			return [];
		}

		var batches = new List<IconImportBatchFile>();
		foreach (var batchDirectory in Directory.EnumerateDirectories(_directory))
		{
			var path = Path.Combine(batchDirectory, BatchFileName);
			var batchId = Path.GetFileName(batchDirectory);

			IconImportBatchFile? batch;
			if (Guid.TryParse(batchId, out var id))
			{
				lock (LockFor(id))
				{
					batch = _files.Read<IconImportBatchFile>(path);
				}
			}
			else
			{
				batch = _files.Read<IconImportBatchFile>(path);
			}

			if (batch is not null)
			{
				batches.Add(batch);
			}
		}

		return batches;
	}

	public void Save(IconImportBatchFile batch)
	{
		lock (LockFor(batch.Id))
		{
			try
			{
				var batchDirectory = Path.Combine(_directory, batch.Id.ToString());
				Directory.CreateDirectory(batchDirectory);
				_files.Write(Path.Combine(batchDirectory, BatchFileName), batch);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write icon import batch {BatchId}", batch.Id);
			}
		}
	}

	public void Delete(Guid batchId)
	{
		lock (LockFor(batchId))
		{
			try
			{
				var path = Path.Combine(_directory, batchId.ToString(), BatchFileName);
				_files.Delete(path);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to delete icon import batch {BatchId}", batchId);
			}
		}
	}

	private object LockFor(Guid id) => _locks.GetOrAdd(id, _ => new object());
}
