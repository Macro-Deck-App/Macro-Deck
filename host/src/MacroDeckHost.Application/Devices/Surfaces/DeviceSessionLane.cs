using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces;

// A plugin's calls are handled one at a time, so an open awaited while handling its register would wait
// on the host calls its open handler makes; changes run here in order instead, off the caller's path.
public sealed class DeviceSessionLane
{
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, Task> _tails = [];
	private readonly ILogger _logger;

	public DeviceSessionLane(ILogger logger)
	{
		_logger = logger.ForContext<DeviceSessionLane>();
	}

	public Task Enqueue(Guid deviceId, string operation, Func<Task> work)
	{
		lock (_gate)
		{
			var next = RunAfterAsync(_tails.GetValueOrDefault(deviceId) ?? Task.CompletedTask,
				deviceId,
				operation,
				work);
			_tails[deviceId] = next;
			_ = next.ContinueWith(_ => Forget(deviceId, next),
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
			return next;
		}
	}

	private async Task RunAfterAsync(Task previous, Guid deviceId, string operation, Func<Task> work)
	{
		await previous;
		await Task.Yield();
		try
		{
			await work();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to {Operation} the session of device {DeviceId}", operation, deviceId);
		}
	}

	private void Forget(Guid deviceId, Task finished)
	{
		lock (_gate)
		{
			if (_tails.TryGetValue(deviceId, out var tail) && tail == finished)
			{
				_tails.Remove(deviceId);
			}
		}
	}
}
