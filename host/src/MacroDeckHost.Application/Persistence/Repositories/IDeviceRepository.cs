using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IDeviceRepository
{
	Task<DeviceEntity?> GetById(Guid id);

	Task<IReadOnlyList<DeviceEntity>> GetAll();

	Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId);

	Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId);

	Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId);

	Task Create(DeviceEntity device);

	Task Update(DeviceEntity device);

	Task Delete(Guid id);

	Task TouchLastSeen(IReadOnlyCollection<Guid> ids, DateTime seenAt);

	Task<IReadOnlyList<DeviceEntity>> GetStale(DateTime lastSeenBefore);

	Task DeleteMany(IReadOnlyCollection<Guid> ids);
}
