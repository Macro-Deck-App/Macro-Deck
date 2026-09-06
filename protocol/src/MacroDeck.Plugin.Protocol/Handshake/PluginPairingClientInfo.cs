namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Optional metadata attached to a <see cref="PluginPairingRequest" />. Every field is
/// self-reported by the requesting process and is not verified by the host.</summary>
public sealed record PluginPairingClientInfo
{
	public string? ExecutablePath { get; init; }

	public int? ProcessId { get; init; }

	public string? SdkVersion { get; init; }
}
