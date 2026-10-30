using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Caching;

public interface IProfileCache
{
	Task InitializeCache();

	bool HadUnreadableProfiles { get; }

	ProfileEntity? GetById(Guid id);

	List<ProfileEntity> GetAll();

	Task AddOrUpdate(ProfileEntity profile);

	Task<IReadOnlyList<ProfileEntity>> ApplyOrders(IReadOnlyDictionary<Guid, int> orders);

	Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders);

	Task<IReadOnlyList<FolderEntity>> Remove(Guid id);

	// Null when the profile is the last one. The check and the removal must be one step, or two deletes
	// running together can each see the other's profile and remove both.
	async Task<IReadOnlyList<FolderEntity>?> RemoveUnlessLast(Guid id)
	{
		if (GetAll().Count <= 1)
		{
			return null;
		}

		return await Remove(id);
	}

	Task AddOrUpdateAggregate(
		ProfileEntity profile,
		IReadOnlyCollection<FolderEntity> folders,
		SecretApprovalWrite write)
	{
		return AddOrUpdateAggregate(profile, folders);
	}
}
