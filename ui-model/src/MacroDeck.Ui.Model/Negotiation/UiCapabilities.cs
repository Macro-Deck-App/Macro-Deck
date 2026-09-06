using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Negotiation;

/// <summary>
/// What a renderer declares it can do. Negotiated non-fatally, day one: a capability this renderer
/// lacks degrades rather than failing the session, exactly as the plugin protocol's own capability
/// negotiation does for its declared capabilities.
/// </summary>
public sealed record UiCapabilities
{
	/// <summary>The UI model protocol range this renderer speaks.</summary>
	[JsonPropertyOrder(0)]
	public required UiVersionRange UiProtocol { get; init; }

	/// <summary>
	/// The Phase-1 trivially-everything answer: a dictionary cannot say "everything", one bool can, and
	/// it stays meaningful once named component ranges exist too, for example for a debug inspector
	/// that always wants the honest answer.
	/// </summary>
	[JsonPropertyOrder(1)]
	public required bool SupportsAllComponents { get; init; }

	/// <summary>
	/// The version range this renderer speaks for each named component it knows about, keyed by the
	/// component's <c>type</c> string. A component absent from this map is unsupported unless
	/// <see cref="SupportsAllComponents" /> is <c>true</c>. Always written, including empty.
	/// </summary>
	/// <remarks>Not a C# <c>required</c> member: a missing <c>components</c> key on the wire
	/// deserializes to empty rather than failing.</remarks>
	[JsonPropertyOrder(2)]
	public IReadOnlyDictionary<string, UiVersionRange> Components
	{
		get;
		init => field = value ?? new Dictionary<string, UiVersionRange>(StringComparer.Ordinal);
	} = new Dictionary<string, UiVersionRange>(StringComparer.Ordinal);
}
