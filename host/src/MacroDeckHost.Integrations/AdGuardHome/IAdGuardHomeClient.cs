namespace MacroDeckHost.Integrations.AdGuardHome;

internal interface IAdGuardHomeClient
{
	Task<AdGuardHomeServerStatus> GetStatusAsync(CancellationToken cancellationToken);

	Task<AdGuardHomeServerStatistics> GetStatisticsAsync(CancellationToken cancellationToken);

	Task SetProtectionAsync(bool enabled, TimeSpan? pause, CancellationToken cancellationToken);

	Task<AdGuardHomeFilteringStatus> GetFilteringStatusAsync(CancellationToken cancellationToken);

	Task SetFilteringAsync(bool enabled, int interval, CancellationToken cancellationToken);

	Task RefreshFiltersAsync(CancellationToken cancellationToken);
}

internal sealed record AdGuardHomeConnectionSettings(
	Uri ControlUrl,
	string? Username,
	string? Password,
	bool AcceptUntrustedCertificate);
