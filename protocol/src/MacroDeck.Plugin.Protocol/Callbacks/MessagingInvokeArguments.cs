using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for the <see cref="HostApis.Messaging" /> api's <c>publish</c>, <c>send</c> and <c>request</c>
/// operations. The host stamps the sender itself; a plugin cannot name one.
/// </summary>
public sealed record MessagingMessageArguments
{
	/// <summary>The topic, for example <c>obs.scene.changed</c>. Never a pattern.</summary>
	public required string Topic { get; init; }

	/// <summary>Any JSON value, or absent. At most <see cref="Limits.MessagingLimits.MaxPayloadBytes" /> serialized.</summary>
	public JsonElement? Payload { get; init; }
}

/// <summary>The result <c>data</c> of a <c>request</c>: the handler's reply.</summary>
public sealed record MessagingReplyPayload
{
	/// <summary>The reply value, or absent when the handler answered with nothing.</summary>
	public JsonElement? Payload { get; init; }
}

/// <summary>
/// Arguments for <c>subscriptions</c>. Complete, not a delta: it replaces everything this plugin
/// subscribed to and handles, so repeating it is harmless.
/// </summary>
public sealed record MessagingSubscriptionsArguments
{
	/// <summary>Event topics or <c>prefix.*</c> patterns.</summary>
	public IReadOnlyList<string> Events { get; init; } = [];

	/// <summary>Command topics this plugin handles.</summary>
	public IReadOnlyList<string> Commands { get; init; } = [];

	/// <summary>Request topics this plugin handles.</summary>
	public IReadOnlyList<string> Requests { get; init; } = [];
}

/// <summary>The result <c>data</c> of <c>subscriptions</c>: the entries the host did not take.</summary>
public sealed record MessagingSubscriptionsResult
{
	public IReadOnlyList<MessagingRejectedTopic> Rejected { get; init; } = [];
}

/// <summary>One entry of <see cref="MessagingSubscriptionsResult.Rejected" />.</summary>
public sealed record MessagingRejectedTopic
{
	/// <summary><c>event</c>, <c>command</c> or <c>request</c>, matching the list the entry came from.</summary>
	public required string Kind { get; init; }

	public required string Topic { get; init; }

	/// <summary>A <c>messaging_</c> value of <c>ProtocolErrorReasons</c>.</summary>
	public required string Reason { get; init; }

	/// <summary>The integration id that already handles the topic, for <c>messaging_topic_handled</c>.</summary>
	public string? Owner { get; init; }
}
