namespace MacroDeckHost.Application.Plugins.Runtime;

// Plugin sockets outlive ApplicationStopping on purpose: the supervisor says goodbye over them while the
// host stops, and a plugin whose socket was aborted first never learns it should exit.
public sealed class PluginConnectionLifetime : IDisposable
{
	private readonly CancellationTokenSource _closing = new();

	public CancellationToken Closing => _closing.Token;

	public void Close()
	{
		_closing.Cancel();
	}

	public void Dispose()
	{
		_closing.Dispose();
	}
}
