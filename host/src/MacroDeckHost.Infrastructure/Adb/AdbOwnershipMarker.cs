using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed record AdbOwnedTunnel(string Serial, int DevicePort, int HostPort);

internal sealed record AdbOwnershipState(
	int ProcessId,
	bool StartedServer,
	IReadOnlyList<AdbOwnedTunnel> Tunnels,
	DateTimeOffset WrittenAt);

internal sealed class AdbOwnershipMarker
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public AdbOwnershipMarker(IMacroDeckPaths paths, ILogger logger)
	{
		_filePath = Path.Combine(paths.ConfigDirectory, "adb-state.json");
		_logger = logger.ForContext<AdbOwnershipMarker>();
		_files = new DurableJsonFile("adb state file", _options, _logger);
	}

	public AdbOwnershipState? Read()
	{
		lock (_lock)
		{
			return _files.Read<AdbOwnershipState>(_filePath);
		}
	}

	public void Write(AdbOwnershipState state)
	{
		lock (_lock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, state);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
			{
				_logger.Debug(ex, "Failed to write adb ownership state to {Path}", _filePath);
			}
		}
	}

	public void Delete()
	{
		lock (_lock)
		{
			_files.Delete(_filePath);
		}
	}
}
