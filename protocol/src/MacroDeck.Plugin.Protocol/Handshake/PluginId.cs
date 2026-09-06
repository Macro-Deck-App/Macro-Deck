using MacroDeck.Sdk.Identity;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// Validates the id a plugin registers under. A plugin is an owner in exactly the sense ADR 0004
/// defines, so this delegates to <see cref="MacroDeckId" /> rather than restating the reverse-domain
/// rule: a second copy of that regex is how the wire protocol and the host would drift apart on what a
/// legal id is.
/// </summary>
public static class PluginId
{
	public const int MaxLength = MacroDeckId.MaxOwnerIdLength;

	public static bool IsValid(string? pluginId) => MacroDeckId.IsValidOwnerId(pluginId, OwnerIdKind.Package);

	/// <summary>Validates and, on failure, explains why in a sentence fit for a protocol error detail.</summary>
	public static bool TryValidate(string? pluginId, out string? error)
		=> MacroDeckId.TryValidateOwnerId(pluginId, OwnerIdKind.Package, out error);
}
