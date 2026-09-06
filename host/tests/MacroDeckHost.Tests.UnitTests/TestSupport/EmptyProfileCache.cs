using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>A profile cache holding nothing, for a test whose collaborator only reads defaults out of
/// it.</summary>
internal sealed class EmptyProfileCache : IProfileCache
{
	public bool HadUnreadableProfiles => false;

	public ProfileEntity? GetById(Guid id) => null;

	public List<ProfileEntity> GetAll() => [];

	public Task InitializeCache() => Task.CompletedTask;

	public Task AddOrUpdate(ProfileEntity profile) => Task.CompletedTask;

	public Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
		=> Task.CompletedTask;

	public Task Remove(Guid id) => Task.CompletedTask;
}
