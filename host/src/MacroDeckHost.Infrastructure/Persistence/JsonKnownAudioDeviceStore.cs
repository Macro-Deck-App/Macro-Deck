using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonKnownAudioDeviceStore : IKnownAudioDeviceStore
{
	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonKnownAudioDeviceStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_filePath = Path.Combine(paths.DataDirectory, "system-audio-devices.json");
		_logger = logger;
		_files = new DurableJsonFile("known audio device file",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public bool TryLoad(out IReadOnlyList<KnownAudioDevice> devices)
	{
		lock (_lock)
		{
			if (_files.Read<List<KnownAudioDevice>>(_filePath) is { } loaded)
			{
				devices = loaded;
				return true;
			}

			devices = [];
			// Read returns null for both an empty data directory and an unrecoverable file; only the former
			// may be treated as an empty list, or the next save would replace the user's pinned names.
			return !File.Exists(_filePath) &&
				!File.Exists(_filePath + DurableJsonFile.TempSuffix) &&
				!File.Exists(_filePath + DurableJsonFile.BackupSuffix);
		}
	}

	public bool Save(IEnumerable<KnownAudioDevice> devices)
	{
		lock (_lock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, devices.ToList());
				return true;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write known audio devices to {Path}", _filePath);
				return false;
			}
		}
	}
}
