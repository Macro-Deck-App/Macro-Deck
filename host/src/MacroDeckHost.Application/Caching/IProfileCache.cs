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

	Task AddOrUpdateAggregate(
		ProfileEntity profile,
		IReadOnlyCollection<FolderEntity> folders,
		SecretApprovalWrite write)
	{
		return AddOrUpdateAggregate(profile, folders);
	}
}
