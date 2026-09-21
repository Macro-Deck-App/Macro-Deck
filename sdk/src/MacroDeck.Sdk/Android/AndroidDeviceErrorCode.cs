namespace MacroDeck.Sdk.Android;

public enum AndroidDeviceErrorCode
{
	/// <summary>A failure this SDK version does not recognise.</summary>
	Unknown = 0,

	/// <summary>ADB is switched off in Macro Deck.</summary>
	AdbNotEnabled = 1,

	/// <summary>ADB is on, but this plugin may not use it.</summary>
	AdbNotAllowed = 2,

	/// <summary>Macro Deck cannot find adb, or cannot reach its server.</summary>
	AdbUnavailable = 3,

	DeviceNotFound = 4,

	DeviceOffline = 5,

	DeviceUnauthorized = 6,

	Timeout = 7,

	/// <summary>adb ran and reported a failure, for example a rejected install.</summary>
	CommandFailed = 8,

	/// <summary>An argument was rejected before anything ran.</summary>
	InvalidArgument = 9,

	/// <summary>There is no connection to Macro Deck.</summary>
	HostUnavailable = 10,

	/// <summary>The host does not offer ADB to plugins, or the device cannot answer this operation.</summary>
	Unsupported = 11,

	/// <summary>Macro Deck is locked and runs nothing that changes a device until it is unlocked.</summary>
	HostLocked = 12,

	/// <summary>Too many calls at once or in quick succession. Retry later.</summary>
	RateLimited = 13
}
