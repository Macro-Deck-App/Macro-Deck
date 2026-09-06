using System.Runtime.CompilerServices;

namespace MacroDeckHost.Integrations.System.Focus;

public sealed class NullFocusedApplicationWatcher : IFocusedApplicationWatcher
{
	private static readonly NullFocusedWindowReader _reader = new();

	public bool IsSupported => false;

	public string? UnsupportedReason => _reader.UnsupportedReason;

	public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await Task.CompletedTask;
		yield break;
	}

	public void Dispose()
	{
	}
}
