namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The stable codes a device session reports a refusal under. Never localized text and never a message
/// to show a user - a provider matches on these, and only these, to decide what to do next.
/// </summary>
public static class DeviceSessionReasons
{
	/// <summary>The widget id is not on the device's current surface, usually because the surface moved
	/// on between the press and its report. Re-render the newest surface and press again.</summary>
	public const string WidgetNotOnSurface = "DEVICE_WIDGET_NOT_ON_SURFACE";

	/// <summary>The host is locked and runs nothing on a device's behalf until it is unlocked.</summary>
	public const string HostLocked = "HOST_LOCKED";

	/// <summary>The host no longer holds this session; the provider is about to be told it closed.</summary>
	public const string SessionNotFound = "DEVICE_SESSION_NOT_FOUND";

	/// <summary>The interaction resolved to a widget trigger, but the host's own pipeline did not run it -
	/// most often because the widget was edited or deleted between the surface push and the press. Nothing
	/// ran; the session stays open.</summary>
	public const string TriggerFailed = "DEVICE_TRIGGER_FAILED";

	/// <summary>The icon does not fit the session's asset transfer bound and cannot be delivered.</summary>
	public const string IconTooLarge = "DEVICE_ICON_TOO_LARGE";
}
