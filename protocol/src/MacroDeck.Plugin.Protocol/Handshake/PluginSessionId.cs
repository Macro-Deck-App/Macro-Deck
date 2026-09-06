using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// Validates a session id: a canonical lowercase dashed UUIDv7. Carried as a plain <c>string</c> on
/// every DTO rather than a wrapper type, so a bad id is a validation failure at the boundary, never a
/// deserializer exception.
/// </summary>
public static partial class PluginSessionId
{
	[GeneratedRegex(@"\A[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex Pattern();

	public static bool IsValid(string? sessionId) => sessionId is not null && Pattern().IsMatch(sessionId);
}
