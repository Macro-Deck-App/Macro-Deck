namespace MacroDeckHost.Application.Devices;

public static class DeviceDefaults
{
	public const int MaxNameLength = 64;

	public const string FallbackName = "Unknown device";

	public const int PresenceLingerSeconds = 15;

	public static readonly TimeSpan StaleDeviceRetention = TimeSpan.FromDays(60);
}
