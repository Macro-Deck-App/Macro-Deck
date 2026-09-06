namespace MacroDeckHost.Domain.Enums;

public enum DeviceClientType
{
	Unknown = 0,
	WebClient = 1,
	AdminUi = 2,
	Native = 3,

	/// <summary>Registered by a device provider rather than by a client that logs in.</summary>
	Provider = 4
}
