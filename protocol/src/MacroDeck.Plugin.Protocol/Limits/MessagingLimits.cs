namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>Bounds of the <c>messaging</c> host api and capability kind.</summary>
public static class MessagingLimits
{
	/// <summary>The largest serialized payload of one event, command, request or reply.</summary>
	public const int MaxPayloadBytes = 64 * 1024;

	public const int MaxTopicLength = 128;

	/// <summary>Per list of a <c>subscriptions</c> call: events, commands and requests each.</summary>
	public const int MaxSubscriptionsPerKind = 256;

	/// <summary>The longest a command or request may wait for its handler, and the default.</summary>
	public static readonly TimeSpan MaxHandlerTimeout = ProtocolTimeouts.CapabilityInvoke;
}
