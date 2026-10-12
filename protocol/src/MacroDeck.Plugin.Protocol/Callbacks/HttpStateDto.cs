namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> payload pushed for <see cref="HostApis.Http" />: what the plugin's outgoing HTTP and
/// WebSocket requests should carry. Sent on registration and whenever the user changes it. An SDK that
/// predates it ignores the push.
/// </summary>
public sealed record HttpStateDto
{
	/// <summary>The User-Agent the user configured, or the host's default when none is configured.</summary>
	public string UserAgent { get; init; } = string.Empty;
}
