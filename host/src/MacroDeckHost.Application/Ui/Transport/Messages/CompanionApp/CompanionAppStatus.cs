namespace MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;

public sealed class CompanionAppStatus
{
	public string? LatestVersion { get; init; }

	public int? LatestVersionCode { get; init; }

	public long? PublishedAt { get; init; }

	public long? LastCheckedAt { get; init; }

	public bool Checking { get; init; }

	public bool CheckFailed { get; init; }

	public bool AutoUpdate { get; init; }

	public bool AdbEnabled { get; init; }

	public IReadOnlyList<CompanionAppDevice> Devices { get; init; } = [];

	public IReadOnlyList<CompanionAppConnectedApp> ConnectedApps { get; init; } = [];
}

public sealed class CompanionAppDevice
{
	public required string Serial { get; init; }

	public required string Name { get; init; }

	public required string State { get; init; }

	public string? InstalledVersion { get; init; }

	public int? InstalledVersionCode { get; init; }

	public string? Error { get; init; }
}

public sealed class CompanionAppConnectedApp
{
	public required Guid DeviceId { get; init; }

	public required string Name { get; init; }

	public string? Model { get; init; }

	public string? AppVersion { get; init; }

	public bool UpdateAvailable { get; init; }
}

public static class CompanionAppDeviceStates
{
	public const string NotAuthorized = "NotAuthorized";
	public const string Checking = "Checking";
	public const string NotAndroid = "NotAndroid";
	public const string DeviceTooOld = "DeviceTooOld";
	public const string NotInstalled = "NotInstalled";
	public const string Installed = "Installed";
	public const string UpToDate = "UpToDate";
	public const string UpdateAvailable = "UpdateAvailable";
	public const string InstalledFromPlayStore = "InstalledFromPlayStore";
	public const string Installing = "Installing";
	public const string Unknown = "Unknown";
}

public static class CompanionAppErrors
{
	public const string AdbDisabled = "AdbDisabled";
	public const string DeviceNotReady = "DeviceNotReady";
	public const string NotAndroid = "NotAndroid";
	public const string DeviceTooOld = "DeviceTooOld";
	public const string NoRelease = "NoRelease";
	public const string DownloadFailed = "DownloadFailed";
	public const string VerificationFailed = "VerificationFailed";
	public const string IncompatibleSignature = "IncompatibleSignature";
	public const string InstallBlockedOnDevice = "InstallBlockedOnDevice";
	public const string StorageFull = "StorageFull";
	public const string InstallFailed = "InstallFailed";
	public const string PlayStoreInstall = "PlayStoreInstall";
}
