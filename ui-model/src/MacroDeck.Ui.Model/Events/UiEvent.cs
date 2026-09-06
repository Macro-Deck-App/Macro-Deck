using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Events;

/// <summary>
/// One event a node raised. This is the event's body only - the plugin protocol's own envelope carries
/// <c>{sessionId, nodeId, event}</c>, so this record carries no session id of its own.
/// </summary>
public sealed record UiEvent
{
	/// <summary>The id of the node that raised the event.</summary>
	[JsonPropertyOrder(0)]
	public required string NodeId { get; init; }

	/// <summary>The event's name, an arbitrary profile-defined string. This model declares no event
	/// vocabulary of its own - a node's <c>Type</c> profile defines which events it emits.</summary>
	[JsonPropertyOrder(1)]
	public required string Name { get; init; }

	/// <summary>Arbitrary event payload data, written and read verbatim including its member order.
	/// Omitted when absent.</summary>
	[JsonPropertyOrder(2)]
	public JsonElement? Data { get; init; }

	/// <summary>The tree revision the client held when the event fired, so the host can discard an
	/// event raised against a node that no longer exists. Omitted when absent.</summary>
	[JsonPropertyOrder(3)]
	public int? Revision { get; init; }
}
