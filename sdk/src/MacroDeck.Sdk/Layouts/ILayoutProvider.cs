namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// Implemented by integrations that describe the surface of a device or client - a control surface, a
/// macro pad, a compact mobile client. A layout says what a surface *is*; it never says what is on it.
/// Devices reference a registered layout by its qualified id, so hardware support needs no device-specific
/// code in Macro Deck itself.
/// </summary>
/// <remarks>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized, and
/// withdraws whatever the provider registered when the integration stops. There is deliberately no
/// shutdown member of its own: <c>IDeviceProvider.ShutdownAsync</c> has the same signature, so a plugin
/// that provides both devices and layouts - the ordinary case for hardware - would have had one method
/// body serving both interfaces and run twice. Release whatever <see cref="InitializeAsync" /> acquired
/// in the integration's own <c>ShutdownAsync</c> instead.
///
/// Withdrawing a provider's layouts does not unconstrain the profiles built against them: the host keeps
/// the last layout it resolved for each device, so a stopped provider never silently frees a grid its
/// hardware still has.
/// </remarks>
public interface ILayoutProvider
{
	/// <summary>Human-readable provider name, e.g. "Stream Deck".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// Registers whatever layouts the provider offers, and keeps registering and withdrawing them as its
	/// hardware or configuration changes.
	/// </summary>
	Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>
	/// The layouts this provider currently offers. The host reads this to recover its view after a
	/// reconnect; a provider that keeps no catalog of its own may return an empty list.
	/// </summary>
	IReadOnlyList<LayoutDescriptor> GetLayouts() => [];
}
