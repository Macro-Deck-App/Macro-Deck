using System.Text.Json;
using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Twitch;

internal sealed class TwitchEventEmitter
{
	private readonly IEventPublisher _publisher;

	public TwitchEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Publish(TwitchAccount account, TwitchEventSubMessage message)
	{
		var payload = message.Payload.ValueKind is JsonValueKind.Object &&
			message.Payload.TryGetProperty("event", out var eventElement)
				? eventElement
				: default;

		var eventId = ResolveEventId(account, message, payload);
		if (eventId is not null && TwitchEventDefinitions.PayloadNames.TryGetValue(eventId, out var declared))
		{
			_publisher.Publish(eventId, TwitchEventPayload.Build(declared, payload, account));
		}

		_publisher.Publish(TwitchEventIds.Any,
			TwitchEventPayload.BuildGeneric(message.SubscriptionType ?? string.Empty,
				message.SubscriptionVersion,
				message.MessageId,
				payload,
				account));
	}

	public void PublishConnection(TwitchAccount account, bool connected)
		=> _publisher.Publish(connected ? TwitchEventIds.Connected : TwitchEventIds.Disconnected,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["account"] = account.UserId,
				["accountLogin"] = account.Login,
				["accountName"] = account.DisplayName
			});

	private static string? ResolveEventId(
		TwitchAccount account,
		TwitchEventSubMessage message,
		JsonElement payload)
	{
		if (string.Equals(message.SubscriptionType, "channel.raid", StringComparison.Ordinal))
		{
			var target = payload.ValueKind is JsonValueKind.Object &&
				payload.TryGetProperty("to_broadcaster_user_id", out var to) &&
				to.ValueKind is JsonValueKind.String
					? to.GetString()
					: null;

			return string.Equals(target, account.UserId, StringComparison.Ordinal)
				? TwitchEventIds.RaidIncoming
				: TwitchEventIds.RaidOutgoing;
		}

		return TwitchEventCatalog.ForType(message.SubscriptionType, message.SubscriptionVersion)?.EventId;
	}
}
