namespace MacroDeckHost.Application.Ui.Transport;

public static class UiDeviceGroups
{
	public static string For(Guid deviceId) => $"ui-device:{deviceId:D}";
}

public static class UiAdminGroups
{
	public const string Admin = "ui-admin";
}
