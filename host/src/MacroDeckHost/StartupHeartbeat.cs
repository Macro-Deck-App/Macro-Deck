namespace MacroDeckHost;

internal sealed class StartupHeartbeat : IDisposable
{
	// Keep in sync with HOST_HEARTBEAT_LINE in ui/bootstrapper/src/host.rs: the bootstrapper extends
	// its ready timeout from the last line it read, so a long restore apply is not killed.
	public const string Line = "macrodeck-host-heartbeat";

	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

	private readonly Lock _gate = new();
	private readonly TextWriter _output;
	private readonly Timer _timer;
	private bool _disposed;

	private StartupHeartbeat(TextWriter output, TimeSpan interval)
	{
		_output = output;
		_timer = new Timer(_ => Beat(), null, interval, interval);
	}

	public static StartupHeartbeat Start(TextWriter output, TimeSpan interval)
	{
		return new StartupHeartbeat(output, interval);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			_timer.Dispose();
		}
	}

	private void Beat()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			try
			{
				_output.WriteLine(Line);
				_output.Flush();
			}
			catch (IOException)
			{
			}
		}
	}
}
