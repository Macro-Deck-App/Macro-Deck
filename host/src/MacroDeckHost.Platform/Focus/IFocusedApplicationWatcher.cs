namespace MacroDeckHost.Platform.Focus;

public interface IFocusedApplicationWatcher : IDisposable
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	bool IsDegraded => false;

	IAsyncEnumerable<FocusedAppInfo> WatchAsync(CancellationToken cancellationToken);
}
