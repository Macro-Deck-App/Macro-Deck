using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginAdbInvokeRunner(
	IPluginCallbackRouter router,
	PluginAdbCallbacks callbacks,
	ILogger logger)
{
	public const int MaxInFlightPerPlugin = 4;

	private readonly ConcurrentDictionary<string, int> _inFlightByPlugin = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<(IPluginConnection Connection, string CorrelationId), CancellationTokenSource> _running
		= new();
	private readonly ILogger _logger = logger.ForContext<PluginAdbInvokeRunner>();

	public async Task StartAsync(IPluginConnection connection,
		string pluginId,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken connectionToken)
	{
		HostCallbackResult? refusal;
		try
		{
			refusal = router.Admit(pluginId, payload) ?? await callbacks.AdmitAsync(pluginId, payload, connectionToken);
		}
		catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
		{
			PluginCallbackRouterLog.CallbackFailed(_logger, payload.Api, payload.Operation, pluginId, exception);
			refusal = HostCallbackResult.Fail(ProtocolErrorCodes.InternalError,
				ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError));
		}

		if (refusal is not null)
		{
			await SendAsync(connection, correlationId, refusal, connectionToken);
			return;
		}

		if (!TryEnter(pluginId))
		{
			await SendAsync(connection,
				correlationId,
				HostCallbackResult.Fail(ProtocolErrorCodes.RateLimited,
					$"This plugin is already running {MaxInFlightPerPlugin} adb calls.",
					retryable: true),
				connectionToken);
			return;
		}

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
		if (!_running.TryAdd((connection, correlationId), cancellation))
		{
			cancellation.Dispose();
			Leave(pluginId);
			await SendAsync(connection,
				correlationId,
				HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, "This correlation id is already in flight."),
				connectionToken);
			return;
		}

		_ = RunAsync(connection, pluginId, correlationId, payload, cancellation);
	}

	public void Cancel(IPluginConnection connection, string? correlationId)
	{
		if (correlationId is not null && _running.TryGetValue((connection, correlationId), out var cancellation))
		{
			try
			{
				cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}
	}

	private async Task RunAsync(IPluginConnection connection,
		string pluginId,
		string correlationId,
		HostInvokePayload payload,
		CancellationTokenSource cancellation)
	{
		try
		{
			await Task.Yield();
			HostCallbackResult result;
			try
			{
				result = await callbacks.ExecuteAsync(payload, cancellation.Token);
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				result = Cancelled();
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				PluginCallbackRouterLog.CallbackFailed(_logger, payload.Api, payload.Operation, pluginId, exception);
				result = HostCallbackResult.Fail(ProtocolErrorCodes.InternalError,
					ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError));
			}

			if (cancellation.IsCancellationRequested)
			{
				result = Cancelled();
			}

			await SendAsync(connection, correlationId, result, CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Could not deliver the adb result for {PluginId}", pluginId);
		}
		finally
		{
			_running.TryRemove((connection, correlationId), out _);
			cancellation.Dispose();
			Leave(pluginId);
		}
	}

	private bool TryEnter(string pluginId)
	{
		while (true)
		{
			var current = _inFlightByPlugin.GetOrAdd(pluginId, 0);
			if (current >= MaxInFlightPerPlugin)
			{
				return false;
			}

			if (_inFlightByPlugin.TryUpdate(pluginId, current + 1, current))
			{
				return true;
			}
		}
	}

	private void Leave(string pluginId) => _inFlightByPlugin.AddOrUpdate(pluginId, 0, (_, current) => Math.Max(0, current - 1));

	private static HostCallbackResult Cancelled()
		=> HostCallbackResult.Fail(ProtocolErrorCodes.Cancelled, ProtocolErrorMessages.For(ProtocolErrorCodes.Cancelled));

	private static Task SendAsync(IPluginConnection connection,
		string correlationId,
		HostCallbackResult result,
		CancellationToken cancellationToken)
		=> connection.Send(new ProtocolEnvelope
			{
				Type = MessageTypes.HostResult,
				Id = Guid.CreateVersion7().ToString(),
				CorrelationId = correlationId,
				Error = result.Error,
				Payload = result.Error is null
					? JsonSerializer.SerializeToElement(new HostResultPayload { Data = result.Data },
						PluginProtocolJson.Options)
					: null
			},
			cancellationToken);
}
