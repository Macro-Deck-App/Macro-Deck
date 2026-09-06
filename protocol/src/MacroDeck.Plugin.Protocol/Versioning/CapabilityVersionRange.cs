namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>An inclusive <c>[Minimum, Maximum]</c> range of versions a declared capability speaks.
/// Negotiated independently per capability kind, on the same algorithm as the session version but with
/// a non-fatal failure policy.</summary>
public sealed record CapabilityVersionRange
{
	public required int Minimum { get; init; }

	public required int Maximum { get; init; }
}
