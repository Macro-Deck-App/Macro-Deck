using MacroDeck.Sdk;

namespace MacroDeckHost.Infrastructure.Integrations;

internal static class IntegrationShutdownRunner
{
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	// A hung InitializeAsync still holds whatever internal lifecycle gate it was blocked on (e.g. Spotify's
	// own gate), and every caller of this method - quitting the host, and retrying the very integration
	// that just timed out - can be asked to shut down that same stuck integration. Task.Run moves the call
	// off the caller's thread for the same reason IntegrationInitializer's does: ShutdownAsync is free to
	// block its calling thread before it ever returns a Task, and without Task.Run that would block the
	// caller for as long as the stuck gate holds, timeout or not.
	public static Task RunAsync(IIntegration integration,
		TimeProvider timeProvider,
		CancellationToken cancellationToken)
		=> Task.Run(() => integration.ShutdownAsync(), cancellationToken)
			.WaitAsync(Timeout, timeProvider, cancellationToken);
}
