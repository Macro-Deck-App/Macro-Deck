using MacroDeckHost.Application.Layouts;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>Populates <see cref="DeviceLayoutConstraintTracker" /> once at startup (issue #384), so it
/// is not empty until the first device event arrives.</summary>
public sealed class DeviceLayoutConstraintWarmupBackgroundService : HostReadyBackgroundService
{
	private readonly DeviceLayoutConstraintTracker _tracker;

	public DeviceLayoutConstraintWarmupBackgroundService(
		IHostApplicationLifetime lifetime,
		DeviceLayoutConstraintTracker tracker)
		: base(lifetime)
	{
		_tracker = tracker;
	}

	protected override Task ExecuteWhenReady(CancellationToken stoppingToken) => _tracker.RefreshAsync(stoppingToken);
}
