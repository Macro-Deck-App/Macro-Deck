namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Notifications"/>'s
/// <c>dismiss</c> operation. <c>notify</c> sends a bare <c>MacroDeck.Sdk.Notifications.UserNotificationRequest</c>
/// as its arguments - it already carries everything the operation needs.</summary>
public sealed record NotificationsDismissArguments
{
	public required string Key { get; init; }
}
