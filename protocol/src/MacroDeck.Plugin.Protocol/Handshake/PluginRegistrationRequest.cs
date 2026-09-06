namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Body of <c>POST /api/plugins/registration</c>, authenticated by the enrollment token header,
/// never by a body field.</summary>
public sealed record PluginRegistrationRequest
{
	/// <summary>Reverse-domain package id, validated by <see cref="PluginId" />.</summary>
	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }
}
