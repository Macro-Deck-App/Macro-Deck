using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>
/// Collects what every handler declares into the one catalogue a session is opened with.
///
/// <para>
/// Rebuilt on each call rather than cached, because a handler's declaration can legitimately change
/// once the user has configured something - which is exactly what a mid-session re-declaration is
/// for, and a cached catalogue would make impossible.
/// </para>
/// </summary>
internal sealed class CapabilityCatalog(IEnumerable<ICapabilityHandler> handlers)
{
	private readonly IReadOnlyList<ICapabilityHandler> _handlers = [.. handlers];

	public IReadOnlyList<ICapabilityHandler> Handlers => _handlers;

	/// <summary>Everything the plugin currently declares, across all handlers.</summary>
	public IReadOnlyList<DeclaredCapability> Declare()
		=> [.. _handlers.SelectMany(handler => handler.DeclareCapabilities())];

	/// <summary>The handler serving <paramref name="kind" />, or null when nothing does.</summary>
	public ICapabilityHandler? Find(string kind)
		=> _handlers.FirstOrDefault(handler => string.Equals(handler.Kind, kind, StringComparison.Ordinal));
}
