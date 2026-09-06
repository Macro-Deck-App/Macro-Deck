using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Sends <c>host.invoke</c> to the host and awaits its <c>host.result</c>. The plugin-side mirror of
/// the host's <c>PluginCapabilityInvoker</c>: the same correlation bookkeeping and timeout/cancel
/// handling, applied to the opposite end of the same exchange. Registered as a singleton, unlike the
/// <see cref="PluginSessionConnection"/> it sends through - see <see cref="PluginConnectionState.ActiveConnection"/>.
/// </summary>
internal sealed class HostInvoker(PluginConnectionState state, TimeProvider timeProvider, ILogger logger)
	: IHostInvoker
{
	private readonly ILogger _logger = logger.ForContext<HostInvoker>();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<ProtocolEnvelope>> _pending
		= new(StringComparer.Ordinal);

	public async Task<JsonElement?> InvokeAsync(string api,
		string operation,
		object? arguments,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(api);
		ArgumentException.ThrowIfNullOrEmpty(operation);

		var connection = state.ActiveConnection;
		if (connection is null)
		{
			throw HostInvocationException.CreateRetryable(ProtocolErrorCodes.CapabilityUnavailable,
				"There is no connection to the host.");
		}

		var correlationId = Guid.CreateVersion7().ToString();

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.HostInvoke,
			Id = correlationId,
			DeadlineMs = (int)ProtocolTimeouts.DefaultRequest.TotalMilliseconds,
			Payload = JsonSerializer.SerializeToElement(new HostInvokePayload
					{ Api = api, Operation = operation, Arguments = SerializeArguments(arguments) },
				PluginProtocolJson.Options)
		};

		var completion = new TaskCompletionSource<ProtocolEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[correlationId] = completion;

		try
		{
			await connection.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
			return await AwaitResultAsync(connection, correlationId, completion, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(correlationId, out _);
		}
	}

	public bool TryComplete(ProtocolEnvelope result)
	{
		var correlationId = result.CorrelationId;
		if (string.IsNullOrEmpty(correlationId))
		{
			return false;
		}

		if (!_pending.TryRemove(correlationId, out var pending))
		{
			return false;
		}

		pending.TrySetResult(result);
		return true;
	}

	private async Task<JsonElement?> AwaitResultAsync(
		PluginSessionConnection connection,
		string correlationId,
		TaskCompletionSource<ProtocolEnvelope> completion,
		CancellationToken cancellationToken)
	{
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = cancellationToken.Register(
			static state => ((TaskCompletionSource)state!).TrySetResult(),
			cancelled);

		var timeoutTask = Task.Delay(ProtocolTimeouts.DefaultRequest, timeProvider, CancellationToken.None);
		var winner = await Task.WhenAny(completion.Task, timeoutTask, cancelled.Task, connection.Ended)
			.ConfigureAwait(false);

		if (ReferenceEquals(winner, cancelled.Task))
		{
			await SendCancelBestEffortAsync(connection, correlationId).ConfigureAwait(false);
			throw new OperationCanceledException(cancellationToken);
		}

		// The reply can no longer arrive, so the request timeout is now dead time - and dead time here
		// is charged to the plugin's own shutdown, since IntegrationLifecycleHostedService.StopAsync
		// waits on the gate an InitializeAsync mid-host.invoke is holding. Waiting it out would overrun
		// the supervisor's graceful budget and turn an ordinary disconnect into a process-tree kill.
		// No host.cancel goes out: the connection it would travel on is the one that just ended. The
		// completion check keeps a host that answers and immediately closes counting as answered -
		// both tasks complete, and WhenAny may report either.
		if (ReferenceEquals(winner, connection.Ended) && !completion.Task.IsCompleted)
		{
			throw HostInvocationException.CreateRetryable(ProtocolErrorCodes.CapabilityUnavailable,
				"The connection to the host ended before the result arrived.");
		}

		if (ReferenceEquals(winner, timeoutTask))
		{
			await SendCancelBestEffortAsync(connection, correlationId).ConfigureAwait(false);
			throw HostInvocationException.CreateRetryable(ProtocolErrorCodes.Timeout,
				ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout));
		}

		var resultEnvelope = await completion.Task.ConfigureAwait(false);
		return ToResult(resultEnvelope);
	}

	private async Task SendCancelBestEffortAsync(PluginSessionConnection connection, string correlationId)
	{
		try
		{
			await connection.SendAsync(new ProtocolEnvelope
					{
						Type = MessageTypes.HostCancel,
						Id = Guid.CreateVersion7().ToString(),
						CorrelationId = correlationId,
						Payload = JsonSerializer.SerializeToElement(
							new HostCancelPayload { Reason = "The caller cancelled or timed out." },
							PluginProtocolJson.Options)
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.HostCancelDeliveryFailed(correlationId, exception);
		}
	}

	private static JsonElement? SerializeArguments(object? arguments)
		=> arguments switch
		{
			null => null,
			JsonElement element => element,
			_ => JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
		};

	private static JsonElement? ToResult(ProtocolEnvelope envelope)
	{
		if (envelope.Error is { } error)
		{
			throw HostInvocationException.From(error);
		}

		return envelope.Payload?.Deserialize<HostResultPayload>(PluginProtocolJson.Options)?.Data;
	}
}
