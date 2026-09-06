namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>The pairing block of <see cref="PluginProtocolDescriptor" />.</summary>
public sealed record PluginPairingDescriptor
{
	public required bool Supported { get; init; }

	public required int RequestLifetimeSeconds { get; init; }

	public required int PollIntervalSeconds { get; init; }

	/// <summary>Whether the host would accept a pairing request right now, which is a live setting a
	/// user can flip at any time - unlike <see cref="Supported" />, which says only that this host
	/// implements pairing at all. Null means the host does not report it, so a caller must attempt the
	/// request to find out; not required, so an older host that omits it still deserialises - this
	/// property must never become a compatibility break for existing plugins.</summary>
	public bool? DeveloperModeEnabled { get; init; }
}
