using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities.Messaging;

/// <summary>Arguments of every <c>messaging</c> <c>capability.invoke</c>: one message for this plugin.</summary>
public sealed record MessagingDeliveryArguments
{
	public required string Topic { get; init; }

	/// <summary>The integration id of the participant that sent the message, stamped by the host.</summary>
	public required string Sender { get; init; }

	/// <summary>Unique per message, minted by the host.</summary>
	public required string MessageId { get; init; }

	public required DateTimeOffset SentAt { get; init; }

	public JsonElement? Payload { get; init; }
}

/// <summary>The result <c>data</c> of a <c>request</c> delivery: the reply the handler returned.</summary>
public sealed record MessagingDeliveryResult
{
	public JsonElement? Payload { get; init; }
}
