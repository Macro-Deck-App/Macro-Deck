using System.Threading.Channels;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions;

// Enqueueing is synchronous because the caller is the plugin connection's serial inbound pump:
// awaiting a client write there would stall every other message that connection has in flight, and
// firing the writes off unordered would let a patch overtake the snapshot it applies to and break the
// revision chain the client validates.
// Attaching a client is enqueued as one operation - deliver the tree, then join the fan-out group - so
// there is no window in which a group patch reaches a client that does not yet hold a tree.
public sealed class UiSessionWorkPump : IAsyncDisposable
{
	private readonly Channel<Func<CancellationToken, Task>> _operations =
		Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
		{
			SingleReader = true, AllowSynchronousContinuations = false
		});

	private readonly CancellationTokenSource _stopping = new();
	private readonly Task _pump;
	private readonly ILogger _logger;
	private readonly string _sessionId;

	public UiSessionWorkPump(string sessionId, ILogger logger)
	{
		_sessionId = sessionId;
		_logger = logger;
		_pump = Task.Run(RunAsync);
	}

	public void Enqueue(Func<CancellationToken, Task> operation) => _operations.Writer.TryWrite(operation);

	// Stops accepting work and waits for everything already enqueued to be written out, so a
	// terminal message a client must see is never dropped by the shutdown that follows it.
	public async ValueTask DisposeAsync()
	{
		_operations.Writer.TryComplete();

		try
		{
			await _pump.ConfigureAwait(false);
		}
		finally
		{
			_stopping.Dispose();
		}
	}

	private async Task RunAsync()
	{
		try
		{
			await foreach (var operation in _operations.Reader.ReadAllAsync().ConfigureAwait(false))
			{
				try
				{
					await operation(_stopping.Token).ConfigureAwait(false);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					UiSessionLog.OutboundOperationFailed(_logger, _sessionId, exception);
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.OutboundOperationFailed(_logger, _sessionId, exception);
		}
	}
}

internal static class UiSessionLog
{
	public static void OutboundOperationFailed(ILogger logger, string sessionId, Exception exception)
		=> logger.Error(exception, "Failed to deliver a UI session message for session '{SessionId}'", sessionId);

	public static void ProviderReportedFault(ILogger logger,
		string sessionId,
		string providerId,
		string code,
		string? message)
		=> logger.Error("The UI provider '{ProviderId}' faulted session '{SessionId}' with '{Code}': {ProviderMessage}",
			providerId,
			sessionId,
			code,
			message ?? "(no message)");

	public static void ProviderCallFailed(ILogger logger, string sessionId, string operation, Exception exception)
		=> logger.Error(exception,
			"The UI provider for session '{SessionId}' failed during '{Operation}'",
			sessionId,
			operation);
}
