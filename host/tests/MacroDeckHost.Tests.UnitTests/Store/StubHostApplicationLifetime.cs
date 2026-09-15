using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Store;

internal sealed class StubHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
	private readonly CancellationTokenSource _started = new();
	private readonly CancellationTokenSource _stopping = new();

	public StubHostApplicationLifetime(bool started = false)
	{
		if (started)
		{
			_started.Cancel();
		}
	}

	public CancellationToken ApplicationStarted => _started.Token;

	public CancellationToken ApplicationStopping => _stopping.Token;

	public CancellationToken ApplicationStopped => CancellationToken.None;

	public void StopApplication() => _stopping.Cancel();

	public void Dispose()
	{
		_started.Dispose();
		_stopping.Dispose();
	}
}
