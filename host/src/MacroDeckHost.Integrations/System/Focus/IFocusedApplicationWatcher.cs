namespace MacroDeckHost.Integrations.System.Focus;

public interface IFocusedApplicationWatcher : IDisposable
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	IAsyncEnumerable<FocusedAppInfo> WatchAsync(CancellationToken cancellationToken);
}
