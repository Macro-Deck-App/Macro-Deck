namespace MacroDeckHost.Application.Notifications;

public sealed record NotificationPrincipal(bool IsAdmin, Guid? DeviceId)
{
	public const string AdminClientId = "admin";

	public string ClientId => DeviceId?.ToString() ?? AdminClientId;
}
