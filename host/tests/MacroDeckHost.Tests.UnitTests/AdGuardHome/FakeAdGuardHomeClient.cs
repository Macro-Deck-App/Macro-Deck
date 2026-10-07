using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

internal sealed class FakeAdGuardHomeClient : IAdGuardHomeClient
{
	public AdGuardHomeServerStatus Status { get; set; } = new(true, true, "v0.107.57", null);

	public AdGuardHomeServerStatistics Statistics { get; set; } = new(1000, 250, 3, 2, 1, 0.012);

	public AdGuardHomeFilteringStatus Filtering { get; set; } = new(true, 24);

	public AdGuardHomeConnection? Failure { get; set; }

	public int DroppedStatusRequests { get; set; }

	public int StatusCalls { get; private set; }

	public int StatisticsCalls { get; private set; }

	public List<(bool Enabled, TimeSpan? Pause)> ProtectionCalls { get; } = [];

	public List<(bool Enabled, int Interval)> FilteringCalls { get; } = [];

	public int RefreshCalls { get; private set; }

	public Task<AdGuardHomeServerStatus> GetStatusAsync(CancellationToken cancellationToken)
	{
		StatusCalls++;
		if (DroppedStatusRequests > 0)
		{
			DroppedStatusRequests--;
			throw new AdGuardHomeException(AdGuardHomeConnection.Unreachable, "dropped");
		}

		ThrowIfFailing();
		return Task.FromResult(Status);
	}

	public Task<AdGuardHomeServerStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
	{
		StatisticsCalls++;
		ThrowIfFailing();
		return Task.FromResult(Statistics);
	}

	public Task SetProtectionAsync(bool enabled, TimeSpan? pause, CancellationToken cancellationToken)
	{
		ThrowIfFailing();
		ProtectionCalls.Add((enabled, pause));
		Status = Status with { ProtectionEnabled = enabled, ProtectionDisabledFor = enabled ? null : pause };
		return Task.CompletedTask;
	}

	public Task<AdGuardHomeFilteringStatus> GetFilteringStatusAsync(CancellationToken cancellationToken)
	{
		ThrowIfFailing();
		return Task.FromResult(Filtering);
	}

	public Task SetFilteringAsync(bool enabled, int interval, CancellationToken cancellationToken)
	{
		ThrowIfFailing();
		FilteringCalls.Add((enabled, interval));
		Filtering = Filtering with { Enabled = enabled };
		return Task.CompletedTask;
	}

	public Task RefreshFiltersAsync(CancellationToken cancellationToken)
	{
		ThrowIfFailing();
		RefreshCalls++;
		return Task.CompletedTask;
	}

	private void ThrowIfFailing()
	{
		if (Failure is { } failure)
		{
			throw new AdGuardHomeException(failure, "fake failure");
		}
	}
}
