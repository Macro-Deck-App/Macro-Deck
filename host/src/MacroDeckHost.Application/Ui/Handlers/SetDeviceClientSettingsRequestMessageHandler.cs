using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class SetDeviceClientSettingsRequestMessageHandler
	: IUiTransportMessageHandler<SetDeviceClientSettingsRequest, SetDeviceClientSettingsResponse>
{
	private readonly IDeviceRepository _devices;
	private readonly IDeviceService _service;

	public SetDeviceClientSettingsRequestMessageHandler(IDeviceRepository devices, IDeviceService service)
	{
		_devices = devices;
		_service = service;
	}

	public async ValueTask<SetDeviceClientSettingsResponse> Handle(
		SetDeviceClientSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var device = await _devices.GetById(request.DeviceId);
		if (device is not { ClientType: DeviceClientType.WebClient })
		{
			return new SetDeviceClientSettingsResponse { Success = false };
		}

		var result = await _service.SetSettingsButtonHidden(request.DeviceId, request.SettingsButtonHidden);
		return new SetDeviceClientSettingsResponse { Success = result.Success };
	}
}
