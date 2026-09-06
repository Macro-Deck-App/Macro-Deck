namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>The result of <see cref="ProtocolVersionNegotiator.Negotiate" />, always carrying the
/// host's own range so a rejected plugin can report something actionable.</summary>
public sealed record ProtocolVersionNegotiationOutcome
{
	public required bool Succeeded { get; init; }

	public int? NegotiatedVersion { get; init; }

	public required ProtocolVersionRange HostRange { get; init; }

	public static ProtocolVersionNegotiationOutcome Success(int negotiatedVersion, ProtocolVersionRange hostRange)
		=> new() { Succeeded = true, NegotiatedVersion = negotiatedVersion, HostRange = hostRange };

	public static ProtocolVersionNegotiationOutcome Failure(ProtocolVersionRange hostRange)
		=> new() { Succeeded = false, HostRange = hostRange };
}
