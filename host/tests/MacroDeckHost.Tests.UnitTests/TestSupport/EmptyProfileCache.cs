using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>A profile cache holding nothing, for a test whose collaborator only reads defaults out of
/// it.</summary>
internal sealed class EmptyProfileCache : IProfileCache
{
	public bool HadUnreadableProfiles => false;

	public ProfileEntity? GetById(Guid id)
	{
		return null;
	}

	public List<ProfileEntity> GetAll()
	{
		return [];
	}

	public Task InitializeCache()
	{
		return Task.CompletedTask;
	}

	public Task AddOrUpdate(ProfileEntity profile)
	{
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<ProfileEntity>> ApplyOrders(IReadOnlyDictionary<Guid, int> orders)
	{
		return Task.FromResult<IReadOnlyList<ProfileEntity>>([]);
	}

	public Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
	{
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<FolderEntity>> Remove(Guid id)
	{
		return Task.FromResult<IReadOnlyList<FolderEntity>>([]);
	}
}
