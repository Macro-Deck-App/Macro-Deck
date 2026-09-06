namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Response of <c>POST /api/plugins/registration</c>. <see cref="PluginSecret" /> is shown
/// exactly once - the host stores only its hash.</summary>
public sealed record PluginRegistrationResponse
{
	public required string PluginId { get; init; }

	public required string PluginSecret { get; init; }
}
