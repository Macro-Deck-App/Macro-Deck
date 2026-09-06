namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>An inclusive <c>[Minimum, Maximum]</c> range of protocol major versions a party speaks.</summary>
public sealed record ProtocolVersionRange
{
	public required int Minimum { get; init; }

	public required int Maximum { get; init; }
}
