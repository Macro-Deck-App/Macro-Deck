using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Negotiation;

/// <summary>
/// An inclusive <c>[Minimum, Maximum]</c> range of integer versions a renderer speaks. One shared type
/// serves both the UI model protocol version (<see cref="UiCapabilities.UiProtocol" />) and each named
/// component (<see cref="UiCapabilities.Components" />): a bare int cannot express "a renderer with no
/// floor" or "a renderer that dropped support for the oldest versions of a component", and widening a
/// bare int into a range later would be a breaking change, not an addition.
/// </summary>
public sealed record UiVersionRange
{
	/// <summary>The oldest version this range accepts.</summary>
	[JsonPropertyOrder(0)]
	public required int Minimum { get; init; }

	/// <summary>The newest version this range accepts.</summary>
	[JsonPropertyOrder(1)]
	public required int Maximum { get; init; }
}
