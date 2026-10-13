namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> payload pushed for <see cref="HostApis.DesktopNotifications" />, per plugin, on registration.
/// </summary>
public sealed record DesktopNotificationsStateDto
{
	/// <summary>Whether this plugin may show desktop notifications and the computer offers them.</summary>
	public bool Supported { get; init; }
}

/// <summary>The arguments of <c>desktop-notifications/show</c>.</summary>
public sealed record DesktopNotificationShowArguments
{
	public string Title { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;
}
