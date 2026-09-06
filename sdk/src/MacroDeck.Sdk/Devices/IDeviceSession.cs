namespace MacroDeck.Sdk.Devices;

/// <summary>
/// One open session between a registered device and the host: the deck surface the host wants the
/// device to render, and the channel through which the device reports hardware interactions back.
/// Handed to <see cref="IDeviceProvider.OnSessionOpenedAsync" /> and safe to retain until it is
/// disposed or <see cref="Closed" /> is raised, whichever comes first.
/// </summary>
public interface IDeviceSession : IAsyncDisposable
{
	/// <summary>The host's global id for the device, matching <see cref="DeviceRegistration.DeviceId" />.</summary>
	string DeviceId { get; }

	/// <summary>The provider-local id the device registered under.</summary>
	string ProviderDeviceId { get; }

	/// <summary>The surface most recently pushed by the host. A provider should render this, and only
	/// this, as of the moment it reads it.</summary>
	DeviceSurface CurrentSurface { get; }

	/// <summary>
	/// Raised whenever the host pushes a new surface. A provider must drop a surface whose
	/// <see cref="DeviceSurface.Revision" /> is not greater than the last one it applied - out-of-order
	/// delivery is possible and this is how a provider recognises and discards it.
	/// </summary>
	event EventHandler<DeviceSurfaceChangedEventArgs>? SurfaceChanged;

	/// <summary>Raised when the session ends, whether by host decision, device disconnect or error.</summary>
	event EventHandler<DeviceSessionClosedEventArgs>? Closed;

	/// <summary>
	/// Reports a hardware interaction to the host. A widget id carried in <paramref name="interaction" />
	/// must come from <see cref="CurrentSurface" /> as the provider currently has it - the host rejects
	/// an id that is not on the device's current surface. The host, never the plugin, resolves the
	/// target and runs the widget's actions.
	/// </summary>
	/// <returns>
	/// The host's verdict. A <see cref="DeviceInteractionStatus.Rejected" /> result - a stale widget id -
	/// and a <see cref="DeviceInteractionStatus.NotSupported" /> one - a kind with no widget model yet -
	/// are both normal outcomes rather than failures: nothing ran, the session stays open, and the next
	/// valid interaction still works.
	/// </returns>
	Task<DeviceInteractionResult> SendInteractionAsync(
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches the bytes for an icon referenced by the current surface. <paramref name="knownETag" />
	/// lets a provider that already has the icon cached skip the transfer; the result's
	/// <see cref="DeviceIconImage.NotModified" /> reports whether that happened.
	/// </summary>
	/// <param name="size">
	/// The pixel size the device renders at. Leaving it null serves the largest rendered variant, never
	/// the original the icon was imported from: a device draws onto a key, and an unbounded master image
	/// would not fit the session's asset transfer bound.
	/// </param>
	/// <returns>Null when no such icon is available to this session.</returns>
	/// <exception cref="DeviceSessionException">
	/// The icon exists but exceeds the session's asset transfer bound
	/// (<see cref="DeviceSessionReasons.IconTooLarge" />). The session stays open.
	/// </exception>
	Task<DeviceIconImage?> GetIconAsync(
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches the bytes behind a widget's currently rendered provider-owned icon - see
	/// <see cref="MacroDeck.Sdk.Devices.DeviceSurfaceAppearance.HasProviderIcon" />. <paramref name="knownETag" />
	/// lets a provider that already has the current bytes cached skip the transfer, exactly as
	/// <see cref="GetIconAsync" /> does for an icon-pack icon. Default-implemented, the same way
	/// <c>IWidgetApi.InvalidateIconAsync</c> is, so an existing implementer compiled before this member
	/// existed keeps compiling; a real session always overrides it.
	/// </summary>
	/// <param name="widgetId">Must come from <see cref="CurrentSurface" /> as the provider currently has
	/// it - the host rejects one that is not.</param>
	/// <returns>
	/// Null when the widget has no provider-owned icon to serve right now - the provider went inactive,
	/// answered blank, or the id is not on this session's current surface.
	/// </returns>
	Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<DeviceWidgetIconImage?>(null);
}
