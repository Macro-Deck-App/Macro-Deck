using System.Runtime.CompilerServices;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Infrastructure.Deck;

public sealed class FocusedApplicationWatcher : IApplicationFocusWatcher, IDisposable
{
	private readonly IFocusedApplicationWatcher _watcher;
	private int _subscribed;

	public FocusedApplicationWatcher()
		: this(FocusedApplicationWatcherFactory.Create())
	{
	}

	// Seam for tests: inject a fake IFocusedApplicationWatcher instead of the real platform one.
	internal FocusedApplicationWatcher(IFocusedApplicationWatcher watcher)
	{
		_watcher = watcher;
	}

	public bool IsSupported => _watcher.IsSupported;

	public string? UnsupportedReason => _watcher.UnsupportedReason;

	public async IAsyncEnumerable<FocusedApplication> WatchAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (Interlocked.CompareExchange(ref _subscribed, 1, 0) != 0)
		{
			throw new InvalidOperationException("Application-focus watcher already has an active subscription.");
		}

		try
		{
			await foreach (var info in _watcher.WatchAsync(cancellationToken))
			{
				FocusedApplicationSnapshot.Current.Set(info);
				yield return new FocusedApplication(info.ProcessId,
					info.ExecutablePath,
					info.ProcessName,
					info.BundleId);
			}
		}
		finally
		{
			Volatile.Write(ref _subscribed, 0);
		}
	}

	public void Dispose() => _watcher.Dispose();
}
