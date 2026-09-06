using System.Runtime.InteropServices;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Observes Ctrl-C (SIGINT) and a supervising process asking this one to stop (SIGTERM) as a single
/// <see cref="CancellationToken" />, via <see cref="PosixSignalRegistration" /> - the modern replacement
/// for <see cref="Console.CancelKeyPress" /> that also covers SIGTERM and Windows' console close event
/// uniformly. <see cref="PosixSignalContext.Cancel" /> is set so the runtime does not terminate the
/// process out from under whichever command is running its own graceful shutdown.
/// </summary>
internal sealed class ShutdownSignal : IDisposable
{
	private readonly CancellationTokenSource _cancellationTokenSource = new();
	private readonly PosixSignalRegistration _sigInt;
	private readonly PosixSignalRegistration _sigTerm;

	public ShutdownSignal()
	{
		_sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Handle);
		_sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handle);
	}

	public CancellationToken Token => _cancellationTokenSource.Token;

	private void Handle(PosixSignalContext context)
	{
		context.Cancel = true;
		_cancellationTokenSource.Cancel();
	}

	public void Dispose()
	{
		_sigInt.Dispose();
		_sigTerm.Dispose();
		_cancellationTokenSource.Dispose();
	}
}
