using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record RenameDeviceBody(string Name);

public record SetDeviceStartupProfileBody(string? ProfileId);

public record OpenProfileOnDeviceBody(string ProfileId);

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetDevicesRequest, GetDevicesResponse> _getDevices;
	private readonly IUiTransportMessageHandler<RenameDeviceRequest, RenameDeviceResponse> _renameDevice;
	private readonly IUiTransportMessageHandler<LogoutDeviceRequest, LogoutDeviceResponse> _logoutDevice;
	private readonly IUiTransportMessageHandler<RemoveDeviceRequest, RemoveDeviceResponse> _removeDevice;

	private readonly IUiTransportMessageHandler<SetDeviceStartupProfileRequest, SetDeviceStartupProfileResponse>
		_setStartupProfile;

	private readonly IUiTransportMessageHandler<OpenProfileOnDeviceRequest, OpenProfileOnDeviceResponse>
		_openProfileOnDevice;

	public DevicesController(
		IUiTransportMessageHandler<GetDevicesRequest, GetDevicesResponse> getDevices,
		IUiTransportMessageHandler<RenameDeviceRequest, RenameDeviceResponse> renameDevice,
		IUiTransportMessageHandler<LogoutDeviceRequest, LogoutDeviceResponse> logoutDevice,
		IUiTransportMessageHandler<RemoveDeviceRequest, RemoveDeviceResponse> removeDevice,
		IUiTransportMessageHandler<SetDeviceStartupProfileRequest, SetDeviceStartupProfileResponse> setStartupProfile,
		IUiTransportMessageHandler<OpenProfileOnDeviceRequest, OpenProfileOnDeviceResponse> openProfileOnDevice)
	{
		_getDevices = getDevices;
		_renameDevice = renameDevice;
		_logoutDevice = logoutDevice;
		_removeDevice = removeDevice;
		_setStartupProfile = setStartupProfile;
		_openProfileOnDevice = openProfileOnDevice;
	}

	[HttpGet]
	public Task<GetDevicesResponse> GetAll(CancellationToken ct)
		=> _getDevices.Handle(new GetDevicesRequest(), ct).AsTask();

	[HttpPatch("{id:guid}/name")]
	public Task<RenameDeviceResponse> Rename(Guid id, RenameDeviceBody body, CancellationToken ct)
		=> _renameDevice.Handle(new RenameDeviceRequest { Id = id, Name = body.Name }, ct).AsTask();

	[HttpPatch("{id:guid}/startup-profile")]
	public Task<SetDeviceStartupProfileResponse> SetStartupProfile(
		Guid id,
		SetDeviceStartupProfileBody body,
		CancellationToken ct)
		=> _setStartupProfile.Handle(new SetDeviceStartupProfileRequest { Id = id, ProfileId = body.ProfileId }, ct)
			.AsTask();

	[HttpPost("{id:guid}/open-profile")]
	public Task<OpenProfileOnDeviceResponse> OpenProfileOnDevice(
		Guid id,
		OpenProfileOnDeviceBody body,
		CancellationToken ct)
		=> _openProfileOnDevice.Handle(new OpenProfileOnDeviceRequest { Id = id, ProfileId = body.ProfileId }, ct)
			.AsTask();

	[HttpPost("{id:guid}/logout")]
	public Task<LogoutDeviceResponse> Logout(Guid id, CancellationToken ct)
		=> _logoutDevice.Handle(new LogoutDeviceRequest { Id = id }, ct).AsTask();

	[HttpDelete("{id:guid}")]
	public Task<RemoveDeviceResponse> Remove(Guid id, CancellationToken ct)
		=> _removeDevice.Handle(new RemoveDeviceRequest { Id = id }, ct).AsTask();
}
