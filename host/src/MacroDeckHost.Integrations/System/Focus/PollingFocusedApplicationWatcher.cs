using System.Runtime.CompilerServices;
using Serilog;

namespace MacroDeckHost.Integrations.System.Focus;

public sealed class PollingFocusedApplicationWatcher : IFocusedApplicationWatcher
{
	private static readonly TimeSpan _defaultInterval = TimeSpan.FromMilliseconds(250);
	private static readonly ILogger _logger = Log.ForContext<PollingFocusedApplicationWatcher>();

	private readonly IFocusedWindowReader _reader;
	private readonly TimeSpan _interval;

	public PollingFocusedApplicationWatcher(IFocusedWindowReader reader)
		: this(reader, _defaultInterval)
	{
	}

	// Seam for tests: inject a poll interval so tests don't wait on the real 250ms cadence.
	internal PollingFocusedApplicationWatcher(IFocusedWindowReader reader, TimeSpan interval)
	{
		_reader = reader;
		_interval = interval;
	}

	public bool IsSupported => _reader.IsSupported;

	public string? UnsupportedReason => _reader.UnsupportedReason;

	public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var first = SafeRead();
		if (first is not null)
		{
			yield return first;
		}

		using var timer = new PeriodicTimer(_interval);
		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			var next = SafeRead();
			if (next is not null)
			{
				yield return next;
			}
		}
	}

	private FocusedAppInfo? SafeRead()
	{
		try
		{
			return _reader.Read();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Focused-window read failed; will retry on the next poll");
			return null;
		}
	}

	public void Dispose()
	{
		if (_reader is IDisposable disposableReader)
		{
			disposableReader.Dispose();
		}
	}
}
