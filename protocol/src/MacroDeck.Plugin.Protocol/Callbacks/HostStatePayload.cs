using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Payload of <c>host.state</c> - the host pushing the list a plugin's synchronous API members serve
/// from, for one <see cref="HostApis" /> member. This is a push, not a reply: it carries no
/// correlation id, because nothing on the plugin side requests it.
/// </summary>
public sealed record HostStatePayload
{
	/// <summary>One of <see cref="HostApis" />.</summary>
	public required string Api { get; init; }

	/// <summary>The pushed state, shaped by the API. Absent when the API has nothing to push.</summary>
	public JsonElement? Data { get; init; }
}
