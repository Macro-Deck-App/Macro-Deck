using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Events;

/// <summary>
/// Payload of <c>event.publish</c> - a plugin raising one of its declared events. Fire-and-forget on
/// the wire, matching <c>IEventPublisher.Publish</c>'s in-process contract: there is no reply message,
/// so a plugin that cannot reach the host learns nothing about it and must not be made to.
/// </summary>
public sealed record EventPublishPayload
{
	/// <summary>The event's unqualified id, e.g. as declared by <c>IEventProvider</c>. The host
	/// qualifies it with the authenticated plugin id, never a value the payload could forge.</summary>
	public required string EventId { get; init; }

	/// <summary>Parameter values, keyed the same way <c>IEventPublisher.Publish</c>'s dictionary is.
	/// Absent when the event carries none.</summary>
	public JsonElement? Parameters { get; init; }
}
