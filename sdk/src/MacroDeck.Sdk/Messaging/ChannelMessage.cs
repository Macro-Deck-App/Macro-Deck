using System.Text.Json;

namespace MacroDeck.Sdk.Messaging;

/// <summary>One message a subscription or handler receives from the message channel.</summary>
public sealed record ChannelMessage
{
	public required ChannelMessageKind Kind { get; init; }

	/// <summary>The concrete topic the message was sent to, never a pattern.</summary>
	public required string Topic { get; init; }

	/// <summary>
	/// The integration id of the participant that sent it: a plugin id, or a built-in integration's id.
	/// Set by Macro Deck, so a handler can rely on it. It equals the receiver's own id for a message the
	/// receiver sent itself, which events matching its own subscriptions are.
	/// </summary>
	public required string Sender { get; init; }

	/// <summary>Unique per message.</summary>
	public required string MessageId { get; init; }

	public required DateTimeOffset SentAt { get; init; }

	/// <summary>The payload as sent, or null when the sender sent none.</summary>
	public JsonElement? Payload { get; init; }

	/// <summary>
	/// Deserializes <see cref="Payload" />. Uses <see cref="JsonSerializerDefaults.Web" /> unless
	/// <paramref name="options" /> is given, and returns the default of <typeparamref name="T" /> when
	/// there is no payload.
	/// </summary>
	/// <exception cref="JsonException">The payload does not fit <typeparamref name="T" />.</exception>
	public T? PayloadAs<T>(JsonSerializerOptions? options = null)
		=> Payload is { } payload
			? payload.Deserialize<T>(options ?? MessageChannelJson.Options)
			: default;
}
