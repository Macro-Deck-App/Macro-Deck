using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Serialization;

/// <summary>
/// The one <see cref="JsonSerializerOptions" /> instance every envelope on the wire is read and
/// written with. Strict by design: the wire is machine-to-machine, unlike a persisted file, so case
/// sensitivity and number strictness catch a misbehaving client instead of silently tolerating it.
/// </summary>
public static class PluginProtocolJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.Strict,
		AllowTrailingCommas = false,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		MaxDepth = ProtocolLimits.MaxJsonDepth,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,

		// Opaque JSON is relayed, not re-encoded - see RawJsonElementConverter. Registered on the one
		// shared options instance rather than per property, because a payload passes through several
		// JsonElement-typed members on its way to the socket and re-encoding at any one of them loses
		// the same bytes.
		Converters = { new RawJsonElementConverter() }
	};
}
