using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Layouts;

/// <summary>
/// Singleton snapshot of the device grid constraint per profile (issue #384), kept in sync by
/// <see cref="RefreshAsync" /> and read synchronously by the profile DTO mapper on the request path.
/// Resilient by design: before the first refresh, or if a refresh fails, <see cref="Get" /> simply
/// returns null rather than throwing - a profile with a stale or missing constraint stays editable
/// rather than blocking the UI.
/// </summary>
public sealed class DeviceLayoutConstraintTracker
{
	private readonly IServiceScopeFactory _scopeFactory;

	private volatile Dictionary<string, DeviceGridConstraint> _snapshot =
		new(StringComparer.Ordinal);

	public DeviceLayoutConstraintTracker(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public DeviceGridConstraint? Get(string profileId)
		=> !string.IsNullOrEmpty(profileId) && _snapshot.TryGetValue(profileId, out var constraint)
			? constraint
			: null;

	public async Task RefreshAsync(CancellationToken ct = default)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var deviceRepository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var devices = await deviceRepository.GetAll();
		var next = new Dictionary<string, DeviceGridConstraint>(StringComparer.Ordinal);

		foreach (var group in devices
			.Where(d => !string.IsNullOrEmpty(d.StartupProfileId))
			.GroupBy(d => d.StartupProfileId!))
		{
			var constraint = DeviceLayoutConstraintResolver.Resolve((IReadOnlyList<DeviceEntity>)group.ToList());
			if (constraint is not null)
			{
				next[group.Key] = constraint;
			}
		}

		// Swapping the reference, rather than mutating a shared ConcurrentDictionary in place, is what
		// makes a profile whose last claiming device just went away drop out of the snapshot instead of
		// keeping a stale entry forever.
		_snapshot = next;
	}
}
