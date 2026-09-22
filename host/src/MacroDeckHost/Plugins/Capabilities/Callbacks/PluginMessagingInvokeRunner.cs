using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Messaging;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.Plugins;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginMessagingInvokeRunner
{
	public const int MaxInFlightPerPlugin = 16;

	private readonly IMessageBroker _broker;
	private readonly PluginMessagingRegistrations _registrations;
	private readonly HostCallbackThrottle _throttle;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<string, int> _inFlightByPlugin = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<(IPluginConnection Connection, string CorrelationId), CancellationTokenSource> _running
		= new();

	public PluginMessagingInvokeRunner(IMessageBroker broker,
		PluginMessagingRegistrations registrations,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_broker = broker;
		_registrations = registrations;
		_throttle = new HostCallbackThrottle(timeProvider, capacity: 100, refillPerSecond: 50);
		_logger = logger.ForContext<PluginMessagingInvokeRunner>();
	}

	public async Task StartAsync(IPluginConnection connection,
		string pluginId,
		string sessionId,
		ProtocolEnvelope envelope,
		HostInvokePayload payload,
		CancellationToken connectionToken)
	{
		var result = Admit(pluginId, payload);
		if (result is null)
		{
			switch (payload.Operation)
			{
				case HostOperations.Messaging.Subscriptions:
					result = Subscriptions(pluginId, sessionId, payload);
					break;

				case HostOperations.Messaging.Publish:
					result = Publish(pluginId, payload);
					break;

				default:
					result = StartHandlerCall(connection, pluginId, envelope, payload, connectionToken);
					break;
			}
		}

		if (result is not null)
		{
			await SendAsync(connection, envelope.Id, result, connectionToken);
		}
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

	private HostCallbackResult? Admit(string pluginId, HostInvokePayload payload)
	{
		if (!HostOperations.IsKnown(payload.Api, payload.Operation))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
				$"The '{payload.Api}' api has no operation '{payload.Operation}'.");
		}

		if (!_registrations.HasDeclaredMessaging(pluginId))
		{
			return Reason(ProtocolErrorCodes.CapabilityUnavailable,
				ProtocolErrorReasons.MessagingNotDeclared,
				"This session did not declare the messaging capability.");
		}

		// subscriptions is bounded by size, not rate: it replaces the whole set and the SDK sends one at a time.
		if (!string.Equals(payload.Operation, HostOperations.Messaging.Subscriptions, StringComparison.Ordinal) &&
			!_throttle.TryConsume(pluginId))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.RateLimited,
				"This plugin is sending messages too quickly.",
				retryable: true);
		}

		return null;
	}

	private HostCallbackResult Subscriptions(string pluginId, string sessionId, HostInvokePayload payload)
	{
		var arguments = Deserialize<MessagingSubscriptionsArguments>(payload);
		if (arguments is null ||
			arguments.Events.Count > MessagingLimits.MaxSubscriptionsPerKind ||
			arguments.Commands.Count > MessagingLimits.MaxSubscriptionsPerKind ||
			arguments.Requests.Count > MessagingLimits.MaxSubscriptionsPerKind)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
				$"subscriptions takes at most {MessagingLimits.MaxSubscriptionsPerKind} entries per list.");
		}

		var (outcome, rejected) = _registrations.Replace(pluginId,
			sessionId,
			new MessageRegistrations(arguments.Events, arguments.Commands, arguments.Requests));

		return outcome switch
		{
			PluginMessagingSyncOutcome.SessionNotCurrent => HostCallbackResult.Fail(ProtocolErrorCodes.SessionNotFound,
				"This session is no longer the plugin's current session.",
				retryable: true),
			PluginMessagingSyncOutcome.NotDeclared => Reason(ProtocolErrorCodes.CapabilityUnavailable,
				ProtocolErrorReasons.MessagingNotDeclared,
				"This session did not declare the messaging capability."),
			_ => HostCallbackResult.Ok(new MessagingSubscriptionsResult
			{
				Rejected =
				[
					.. rejected.Select(rejection => new MessagingRejectedTopic
					{
						Kind = KindName(rejection.Kind),
						Topic = rejection.Topic,
						Reason = rejection.Reason,
						Owner = rejection.Owner
					})
				]
			})
		};
	}

	private HostCallbackResult Publish(string pluginId, HostInvokePayload payload)
	{
		if (Deserialize<MessagingMessageArguments>(payload) is not { } arguments)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, "publish needs a topic.");
		}

		try
		{
			_broker.Publish(pluginId, arguments.Topic, arguments.Payload);
			return HostCallbackResult.Ok();
		}
		catch (MessageChannelException exception)
		{
			return ToResult(exception);
		}
	}

	private HostCallbackResult? StartHandlerCall(IPluginConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		HostInvokePayload payload,
		CancellationToken connectionToken)
	{
		if (Deserialize<MessagingMessageArguments>(payload) is not { } arguments)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, $"{payload.Operation} needs a topic.");
		}

		if (!TryEnter(pluginId))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.RateLimited,
				$"This plugin is already waiting on {MaxInFlightPerPlugin} commands and requests.",
				retryable: true);
		}

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
		if (!_running.TryAdd((connection, envelope.Id), cancellation))
		{
			cancellation.Dispose();
			Leave(pluginId);
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, "This correlation id is already in flight.");
		}

		var timeout = envelope.DeadlineMs is > 0
			? TimeSpan.FromMilliseconds(Math.Min(envelope.DeadlineMs.Value, MessagingLimits.MaxHandlerTimeout.TotalMilliseconds))
			: MessagingLimits.MaxHandlerTimeout;

		_ = RunAsync(connection, pluginId, envelope.Id, payload.Operation, arguments, timeout, cancellation);
		return null;
	}

	// Off the session's dispatch loop: a handler in this same plugin that calls back into the host would
	// otherwise queue its host.invoke behind this call and never answer it.
	private async Task RunAsync(IPluginConnection connection,
		string pluginId,
		string correlationId,
		string operation,
		MessagingMessageArguments arguments,
		TimeSpan timeout,
		CancellationTokenSource cancellation)
	{
		try
		{
			await Task.Yield();
			HostCallbackResult result;
			try
			{
				if (string.Equals(operation, HostOperations.Messaging.Request, StringComparison.Ordinal))
				{
					var reply = await _broker.RequestAsync(pluginId,
						arguments.Topic,
						arguments.Payload,
						timeout,
						cancellation.Token);
					result = HostCallbackResult.Ok(new MessagingReplyPayload { Payload = reply });
				}
				else
				{
					await _broker.SendAsync(pluginId, arguments.Topic, arguments.Payload, timeout, cancellation.Token);
					result = HostCallbackResult.Ok();
				}
			}
			catch (MessageChannelException exception)
			{
				result = ToResult(exception);
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				result = HostCallbackResult.Fail(ProtocolErrorCodes.Cancelled,
					ProtocolErrorMessages.For(ProtocolErrorCodes.Cancelled));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "Routing {Topic} for {PluginId} failed", arguments.Topic, pluginId);
				result = ToResult(new MessageChannelException(MessageChannelErrorCode.HandlerFailed,
					arguments.Topic,
					$"The handler of '{arguments.Topic}' failed."));
			}

			await SendAsync(connection, correlationId, result, CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Could not deliver the messaging result for {PluginId}", pluginId);
		}
		finally
		{
			_running.TryRemove((connection, correlationId), out _);
			cancellation.Dispose();
			Leave(pluginId);
		}
	}

	private static HostCallbackResult ToResult(MessageChannelException exception)
		=> exception.ErrorCode switch
		{
			MessageChannelErrorCode.InvalidTopic => Reason(ProtocolErrorCodes.InvalidPayload,
				ProtocolErrorReasons.MessagingInvalidTopic,
				exception.Message),
			MessageChannelErrorCode.PayloadTooLarge => HostCallbackResult.Fail(ProtocolErrorCodes.PayloadTooLarge,
				exception.Message),
			MessageChannelErrorCode.NoHandler => Reason(ProtocolErrorCodes.CapabilityUnavailable,
				ProtocolErrorReasons.MessagingNoHandler,
				exception.Message),
			MessageChannelErrorCode.HandlerUnavailable => Reason(ProtocolErrorCodes.CapabilityUnavailable,
				ProtocolErrorReasons.MessagingHandlerUnavailable,
				exception.Message,
				retryable: true),
			MessageChannelErrorCode.Timeout => HostCallbackResult.Fail(ProtocolErrorCodes.Timeout,
				exception.Message,
				retryable: true),
			MessageChannelErrorCode.RateLimited => HostCallbackResult.Fail(ProtocolErrorCodes.RateLimited,
				exception.Message,
				retryable: true),
			_ => Reason(ProtocolErrorCodes.CapabilityUnavailable,
				ProtocolErrorReasons.MessagingHandlerFailed,
				exception.Message)
		};

	private static HostCallbackResult Reason(string code, string reason, string message, bool retryable = false)
		=> HostCallbackResult.Fail(code, message, retryable, new Dictionary<string, string> { ["reason"] = reason });

	private static string KindName(ChannelMessageKind kind)
		=> kind switch
		{
			ChannelMessageKind.Command => "command",
			ChannelMessageKind.Request => "request",
			_ => "event"
		};

	private static T? Deserialize<T>(HostInvokePayload payload)
		where T : class
	{
		try
		{
			return payload.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
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
