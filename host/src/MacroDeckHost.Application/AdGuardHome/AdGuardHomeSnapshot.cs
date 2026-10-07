namespace MacroDeckHost.Application.AdGuardHome;

public enum AdGuardHomeConnection
{
	Connecting,

	Connected,

	Unauthorized,

	Unreachable,

	Timeout,

	Incompatible,

	Redirected
}

public sealed record AdGuardHomeStatistics(
	long DnsQueries,
	long BlockedFiltering,
	long SafeBrowsing,
	long SafeSearch,
	long Parental,
	double AverageProcessingMilliseconds)
{
	public double BlockedPercentage => DnsQueries <= 0 ? 0 : BlockedFiltering * 100d / DnsQueries;
}

public sealed record AdGuardHomeSnapshot(
	string EntryId,
	string Title,
	string VariableKey,
	AdGuardHomeConnection Connection)
{
	public bool? ProtectionEnabled { get; init; }

	public bool? DnsRunning { get; init; }

	public DateTimeOffset? DisabledUntil { get; init; }

	public string? Version { get; init; }

	public AdGuardHomeStatistics? Statistics { get; init; }

	public bool IsConnected => Connection == AdGuardHomeConnection.Connected;
}

public enum AdGuardHomeCommandKind
{
	EnableProtection,

	DisableProtection,

	ToggleProtection,

	PauseProtection,

	EnableFiltering,

	DisableFiltering,

	ToggleFiltering,

	RefreshFilters
}

public sealed record AdGuardHomeCommand(AdGuardHomeCommandKind Kind, TimeSpan? Duration = null);

public enum AdGuardHomeCommandOutcome
{
	Succeeded,

	NotFound,

	Unauthorized,

	Unreachable,

	Timeout,

	Incompatible,

	Redirected,

	Failed
}
