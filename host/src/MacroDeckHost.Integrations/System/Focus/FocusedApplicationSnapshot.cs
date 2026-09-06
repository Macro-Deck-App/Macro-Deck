using Serilog;

namespace MacroDeckHost.Integrations.System.Focus;

// A process-wide holder rather than a second IFocusedWindowReader/IApplicationFocusWatcher subscriber:
// SystemIntegration is constructed parameterlessly by platform factories, so giving it its own reader
// would open a second X11 display connection on Linux, and IApplicationFocusWatcher only ever permits
// one active subscription, which the background service already holds.
public sealed class FocusedApplicationSnapshot
{
	public static FocusedApplicationSnapshot Current { get; } = new();

	private static readonly ILogger _logger = Log.ForContext<FocusedApplicationSnapshot>();

	private volatile FocusedAppInfo? _value;

	private FocusedApplicationSnapshot()
	{
	}

	public FocusedAppInfo? Value => _value;

	public event Action<FocusedAppInfo?>? Changed;

	public void Set(FocusedAppInfo? value)
	{
		_value = value;

		try
		{
			Changed?.Invoke(value);
		}
		catch (Exception ex)
		{
			// Set runs on the focus pipeline's own consuming loop (FocusedApplicationWatcher.WatchAsync), so
			// a subscriber exception escaping here would unwind out of that loop instead of just this event.
			_logger.Warning(ex, "A FocusedApplicationSnapshot subscriber threw while handling a focus change");
		}
	}
}
