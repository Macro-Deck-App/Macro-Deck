namespace MacroDeck.Sdk.Devices;

/// <summary>
/// Implemented by integrations that bring their own hardware or custom clients into Macro Deck - a
/// control surface, a macro pad, a network-connected client. The provider discovers devices however its
/// transport requires and registers them through <see cref="IDeviceProviderContext" />; the host owns
/// persistence, global ids, startup profiles and the rest of the device model.
/// </summary>
/// <remarks>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized, and
/// <see cref="ShutdownAsync" /> before it stops. Devices left registered at shutdown are marked offline
/// rather than deleted, so a provider does not have to unregister everything on the way out.
/// </remarks>
public interface IDeviceProvider
{
	/// <summary>Human-readable provider name, e.g. "Stream Deck".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// Starts device discovery. Register whatever is already connected, then keep registering,
	/// updating and unregistering devices as hardware comes and goes.
	/// </summary>
	Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>Stops discovery and releases whatever <see cref="InitializeAsync" /> acquired.</summary>
	Task ShutdownAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// The devices this provider currently offers. The host reads this to recover its view after a
	/// reconnect; a provider that keeps no catalog of its own may return an empty list.
	/// </summary>
	IReadOnlyList<DeviceDescriptor> GetDevices() => [];

	/// <summary>
	/// Called once a registered device has an open <see cref="IDeviceSession" /> to render against and
	/// send interactions back through. A provider that leaves this unimplemented never renders a deck
	/// surface or reports hardware interactions - the default is a no-op so a provider that only cares
	/// about registration is unaffected.
	/// </summary>
	Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
