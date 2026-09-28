using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.References;

/// <summary>
/// A reference to one stream of a video stream provider, written as
/// <c>{"stream":{"provider":"com.example.obs::studio","id":"Program"}}</c>. It never carries a URL or a
/// credential: the reader asks Macro Deck for a session on the stream itself. A reader treats a value it
/// cannot read, such as one with a missing member, as absent.
/// </summary>
public sealed record UiVideoStreamReference
{
	/// <summary>The provider's qualified id as Macro Deck lists it, <c>plugin.id::provider-id</c>.</summary>
	[JsonPropertyOrder(0)]
	public required string Provider { get; init; }

	/// <summary>The stream's id within that provider.</summary>
	[JsonPropertyOrder(1)]
	public required string Id { get; init; }
}
