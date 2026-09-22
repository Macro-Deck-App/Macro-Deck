using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Messaging;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Messaging;
using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Application.Messaging;

public sealed class RemotePluginMessageParticipant(
	string pluginId,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState) : IMessageParticipant
{
	public async Task DeliverEventAsync(MessageDelivery message, CancellationToken cancellationToken)
	{
		if (!connectionState.IsConnected(pluginId))
		{
			return;
		}

		await invoker.InvokeAsync(pluginId, Request(CapabilityOperations.Messaging.Event, message, null), cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task HandleCommandAsync(MessageDelivery message, TimeSpan timeout, CancellationToken cancellationToken)
		=> await InvokeHandlerAsync(CapabilityOperations.Messaging.Command, message, timeout, cancellationToken)
			.ConfigureAwait(false);

	public async Task<JsonElement?> HandleRequestAsync(MessageDelivery message,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var data = await InvokeHandlerAsync(CapabilityOperations.Messaging.Request, message, timeout, cancellationToken)
			.ConfigureAwait(false);
		try
		{
			return data?.Deserialize<MessagingDeliveryResult>(PluginProtocolJson.Options)?.Payload;
		}
		catch (JsonException)
		{
			throw HandlerFailed(message.Topic);
		}
	}

	private async Task<JsonElement?> InvokeHandlerAsync(string operation,
		MessageDelivery message,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		if (!connectionState.IsConnected(pluginId))
		{
			throw Unavailable(message.Topic);
		}

		try
		{
			return await invoker.InvokeAsync(pluginId, Request(operation, message, timeout), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (RemoteCapabilityException exception)
		{
			throw Map(exception, message.Topic);
		}
	}

	private static MessageChannelException Map(RemoteCapabilityException exception, string topic)
	{
		var reason = exception.Details?.GetValueOrDefault("reason");

		return (exception.Code, reason) switch
		{
			(_, ProtocolErrorReasons.MessagingNoHandler) => new MessageChannelException(MessageChannelErrorCode.NoHandler,
				topic,
				$"Nothing handles '{topic}'."),
			(_, ProtocolErrorReasons.MessagingHandlerFailed) => HandlerFailed(topic),
			(_, ProtocolErrorReasons.MessagingHandlerUnavailable) => Unavailable(topic),
			(ProtocolErrorCodes.Timeout, _) => new MessageChannelException(MessageChannelErrorCode.Timeout,
				topic,
				$"The handler of '{topic}' did not answer in time."),
			(ProtocolErrorCodes.PayloadTooLarge, _) => new MessageChannelException(MessageChannelErrorCode.PayloadTooLarge,
				topic,
				$"The reply of '{topic}' is too large."),
			(ProtocolErrorCodes.CapabilityUnavailable or ProtocolErrorCodes.RateLimited or ProtocolErrorCodes.QueueOverflow, _)
				=> Unavailable(topic),
			_ => HandlerFailed(topic)
		};
	}

	private static MessageChannelException Unavailable(string topic)
		=> new(MessageChannelErrorCode.HandlerUnavailable, topic, $"The handler of '{topic}' cannot be reached right now.");

	private static MessageChannelException HandlerFailed(string topic)
		=> new(MessageChannelErrorCode.HandlerFailed, topic, $"The handler of '{topic}' failed.");

	private static CapabilityInvokeRequest Request(string operation, MessageDelivery message, TimeSpan? timeout)
		=> new()
		{
			Kind = CapabilityKinds.Messaging,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Timeout = timeout,
			Arguments = new MessagingDeliveryArguments
			{
				Topic = message.Topic,
				Sender = message.Sender,
				MessageId = message.MessageId,
				SentAt = message.SentAt,
				Payload = message.Payload
			}
		};
}
