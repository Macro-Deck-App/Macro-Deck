using System.Text.Json;

namespace MacroDeck.Plugin.Testing;

/// <summary>One <c>event.publish</c> a plugin under test raised.</summary>
public sealed record PublishedEvent
{
	/// <summary>The event's unqualified id, as declared by the plugin's event provider.</summary>
	public required string EventId { get; init; }

	/// <summary>Parameter values, serialized through <c>PluginProtocolJson.Options</c>. Null when the event carries none.</summary>
	public JsonElement? Parameters { get; init; }

	/// <summary>When the event was collected.</summary>
	public required DateTimeOffset PublishedAt { get; init; }
}
