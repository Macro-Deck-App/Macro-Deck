namespace MacroDeckHost.Integrations.Streaming;

internal sealed class StreamPump : IDisposable
{
	private readonly StreamChatHub _hub;
	private readonly Lock _sync = new();
	private CancellationTokenSource? _stopping;
	private Task? _running;

	public StreamPump(StreamChatHub hub)
	{
		_hub = hub;
	}

	public void Start()
	{
		lock (_sync)
		{
			if (_running is not null)
			{
				return;
			}

			_stopping = new CancellationTokenSource();
			_running = _hub.RunAsync(_stopping.Token);
		}
	}

	public async Task StopAsync()
	{
		CancellationTokenSource? stopping;
		Task? running;

		lock (_sync)
		{
			stopping = _stopping;
			running = _running;
			_stopping = null;
			_running = null;
		}

		if (stopping is not null)
		{
			await stopping.CancelAsync().ConfigureAwait(false);
		}

		if (running is not null)
		{
			await running.ConfigureAwait(false);
		}

		stopping?.Dispose();
		_hub.Tick();
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_stopping?.Cancel();
			_stopping?.Dispose();
			_stopping = null;
			_running = null;
		}
	}
}
