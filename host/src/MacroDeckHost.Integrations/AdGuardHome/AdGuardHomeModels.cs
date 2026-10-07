using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.AdGuardHome;

internal sealed record AdGuardHomeServerStatus(
	bool ProtectionEnabled,
	bool Running,
	string Version,
	TimeSpan? ProtectionDisabledFor);

internal sealed record AdGuardHomeServerStatistics(
	long DnsQueries,
	long BlockedFiltering,
	long SafeBrowsing,
	long SafeSearch,
	long Parental,
	double AverageProcessingSeconds);

internal sealed record AdGuardHomeFilteringStatus(bool Enabled, int Interval);

internal sealed class StatusResponse
{
	[JsonPropertyName("protection_enabled")]
	public bool? ProtectionEnabled { get; init; }

	[JsonPropertyName("protection_disabled_duration")]
	public long? ProtectionDisabledDuration { get; init; }

	[JsonPropertyName("running")]
	public bool? Running { get; init; }

	[JsonPropertyName("version")]
	public string? Version { get; init; }
}

internal sealed class StatsResponse
{
	[JsonPropertyName("num_dns_queries")]
	public long? DnsQueries { get; init; }

	[JsonPropertyName("num_blocked_filtering")]
	public long? BlockedFiltering { get; init; }

	[JsonPropertyName("num_replaced_safebrowsing")]
	public long? SafeBrowsing { get; init; }

	[JsonPropertyName("num_replaced_safesearch")]
	public long? SafeSearch { get; init; }

	[JsonPropertyName("num_replaced_parental")]
	public long? Parental { get; init; }

	[JsonPropertyName("avg_processing_time")]
	public double? AverageProcessingTime { get; init; }
}

internal sealed class FilteringStatusResponse
{
	[JsonPropertyName("enabled")]
	public bool? Enabled { get; init; }

	[JsonPropertyName("interval")]
	public int? Interval { get; init; }
}

internal sealed class ProtectionRequest
{
	[JsonPropertyName("enabled")]
	public required bool Enabled { get; init; }

	[JsonPropertyName("duration")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? Duration { get; init; }
}

internal sealed class FilteringConfigRequest
{
	[JsonPropertyName("enabled")]
	public required bool Enabled { get; init; }

	[JsonPropertyName("interval")]
	public required int Interval { get; init; }
}

internal sealed class FilteringRefreshRequest
{
	[JsonPropertyName("whitelist")]
	public required bool Whitelist { get; init; }
}
