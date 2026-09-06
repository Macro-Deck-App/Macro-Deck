using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Devices;

public record DeviceRegistrationResult(DeviceEntity Device, string? IssuedSecret);

public interface IDeviceService
{
	Task<DeviceRegistrationResult> RegisterOrReuse(DeviceRegistration registration, DateTime now);

	Task PurgeStale(DateTime now);

	Task<IReadOnlyList<Device>> GetAll();

	Task<Device> ToDto(DeviceEntity device);

	Task<Result<DeviceEntity, DeviceError>> Rename(Guid id, string name);

	Task<Result<DeviceError>> LogoutDevice(Guid id);

	Task<Result<DeviceError>> RemoveDevice(Guid id);

	Task<Result<DeviceEntity, DeviceError>> SetStartupProfile(Guid id, string? profileId);

	Task<string?> ResolveStartupProfileId(Guid deviceId);

	Task<Result<DeviceError>> OpenProfileOnDevice(Guid id, string profileId);

	Task ClearStartupProfileAssignments(string profileId);
}
