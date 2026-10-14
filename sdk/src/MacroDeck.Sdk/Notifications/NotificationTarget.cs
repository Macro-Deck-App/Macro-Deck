namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// Which clients a notification is shown on. Devices are named by their device id, the identity Macro
/// Deck authenticates, never by an id a client chose for itself.
/// </summary>
public sealed record NotificationTarget
{
	public static NotificationTarget All { get; } = new() { Kind = NotificationTargetKind.All };

	public static NotificationTarget AdminUi { get; } = new() { Kind = NotificationTargetKind.AdminUi };

	public NotificationTargetKind Kind { get; init; } = NotificationTargetKind.All;

	/// <summary>Only read when <see cref="Kind" /> is <see cref="NotificationTargetKind.Devices" />.</summary>
	public IReadOnlyList<Guid> DeviceIds { get; init; } = [];

	public static NotificationTarget Devices(params Guid[] deviceIds)
	{
		return new NotificationTarget { Kind = NotificationTargetKind.Devices, DeviceIds = deviceIds };
	}
}
