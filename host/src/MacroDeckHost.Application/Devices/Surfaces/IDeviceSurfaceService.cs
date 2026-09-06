using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// Owns every open device session: what each device renders, where it navigated to, and what it may
/// interact with. A session exists per registered provider device and is independent of every other -
/// nothing that happens on one device is ever observed by another, or by a connected client.
/// </summary>
public interface IDeviceSurfaceService
{
	bool IsOpen(Guid deviceId);

	Task OpenAsync(Guid deviceId,
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default);

	Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default);

	/// <summary>Applies one navigation to a single device. False when the target does not resolve, in
	/// which case nothing moved and nothing was pushed.</summary>
	Task<bool> NavigateAsync(Guid deviceId,
		DeckNavigationCommand command,
		CancellationToken cancellationToken = default);

	Task<DeviceInteractionOutcome> SubmitInteractionAsync(
		Guid deviceId,
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default);

	Task<DeviceIconImage?> GetIconAsync(
		Guid deviceId,
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default);

	/// <summary>Fetches the bytes behind a widget's currently rendered action-icon-provider icon.
	/// <paramref name="widgetId" /> must name a widget on the device's current surface. Null when the
	/// device has no open session, the widget is not on its surface, or the widget has no provider-owned
	/// icon to serve right now.</summary>
	Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		Guid deviceId,
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default);

	/// <summary>Rebuilds every session's surface. Only the sessions whose projection actually changed
	/// are pushed to.</summary>
	Task InvalidateAsync(CancellationToken cancellationToken = default);

	Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default);

	/// <summary>An offline device keeps its session and its place; it just stops being pushed to.</summary>
	Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default);
}
