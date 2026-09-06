namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The host surface a layout provider registers against. Handed to
/// <see cref="ILayoutProvider.InitializeAsync" /> and safe to retain until the matching
/// <see cref="ILayoutProvider.ShutdownAsync" /> returns.
/// </summary>
public interface ILayoutProviderContext
{
	/// <summary>
	/// Registers a layout, or replaces one already registered under the same provider-local id. Replacing
	/// is how a layout changes: devices that reference it pick the new descriptor up without
	/// re-registering.
	/// </summary>
	/// <returns>The host-assigned identity, whose <see cref="LayoutRegistration.LayoutId" /> is what a
	/// device's <c>LayoutReference</c> must carry.</returns>
	/// <exception cref="ArgumentException">The descriptor's id or name is empty, or a region id is empty
	/// or repeated within the layout.</exception>
	Task<LayoutRegistration> RegisterLayoutAsync(
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a layout. Devices still referencing it keep their reference and fall back to whatever the
	/// host last persisted for them, so withdrawing a layout never silently unconstrains a profile.
	/// Unknown ids are ignored, so a provider racing a shutdown does not have to guard the call.
	/// </summary>
	/// <param name="layoutId">The provider-local id the layout was registered under.</param>
	Task UnregisterLayoutAsync(string layoutId, CancellationToken cancellationToken = default);
}
