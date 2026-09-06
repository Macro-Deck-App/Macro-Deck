namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// The single local id every provider-shaped capability kind (<c>events</c>, <c>issues</c>,
/// <c>music-player</c>, <c>weather</c>, <c>virtual-profiles</c>, ...) declares - see
/// <see cref="CapabilityOperations" />'s remarks on the two local-id conventions. A shared constant so
/// every SDK handler and the host's validator spell it the same way.
/// </summary>
public static class ProviderCapabilityId
{
	public const string LocalId = "provider";
}
