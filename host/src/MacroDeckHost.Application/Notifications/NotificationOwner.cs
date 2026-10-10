namespace MacroDeckHost.Application.Notifications;

public sealed record NotificationOwner(NotificationSourceCategory Category, string Id, string Name)
{
	public const string HostId = "host";

	public static NotificationOwner System { get; } = new(NotificationSourceCategory.System, HostId, "Macro Deck");
}
