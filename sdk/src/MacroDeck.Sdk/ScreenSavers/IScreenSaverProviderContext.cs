namespace MacroDeck.Sdk.ScreenSavers;

/// <summary>
/// The host surface a screensaver provider registers against. Handed to
/// <see cref="IScreenSaverProvider.InitializeAsync" /> and safe to retain for as long as the integration
/// runs.
/// </summary>
public interface IScreenSaverProviderContext
{
	/// <summary>
	/// Registers a screensaver, or replaces one already registered under the same provider-local id.
	/// Replacing is how a screensaver's name, description or flags change: devices that selected it pick the
	/// new descriptor up without being touched.
	/// </summary>
	/// <returns>The host-assigned identity, whose <see cref="ScreenSaverRegistration.ScreenSaverId" /> is
	/// what a device stores. Against a host that predates screensavers the id is empty and nothing was
	/// registered; the call does not throw.</returns>
	/// <exception cref="ArgumentException">The descriptor's id or name is empty, or the id is not a valid
	/// local id.</exception>
	Task<ScreenSaverRegistration> RegisterScreenSaverAsync(
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a screensaver. Devices that selected it keep their stored id and configuration and show the
	/// built-in clock until it is registered again, so withdrawing never destroys a device's setup. Unknown
	/// ids are ignored, so a provider racing a shutdown does not have to guard the call.
	/// </summary>
	/// <param name="screenSaverId">The provider-local id the screensaver was registered under.</param>
	Task UnregisterScreenSaverAsync(string screenSaverId, CancellationToken cancellationToken = default);
}
