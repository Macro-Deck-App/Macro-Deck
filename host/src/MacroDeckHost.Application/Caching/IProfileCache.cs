using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Caching;

public interface IProfileCache
{
	Task InitializeCache();

	bool HadUnreadableProfiles { get; }

	ProfileEntity? GetById(Guid id);

	List<ProfileEntity> GetAll();

	Task AddOrUpdate(ProfileEntity profile);

	Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders);

	Task Remove(Guid id);
}
