using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ScreenSavers;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetDeviceScreenSaverRequestMessageHandler
	: IUiTransportMessageHandler<GetDeviceScreenSaverRequest, GetDeviceScreenSaverResponse>
{
	private readonly IDeviceRepository _devices;

	public GetDeviceScreenSaverRequestMessageHandler(IDeviceRepository devices)
	{
		_devices = devices;
	}

	public async ValueTask<GetDeviceScreenSaverResponse> Handle(
		GetDeviceScreenSaverRequest request,
		CancellationToken cancellationToken)
	{
		var device = request.DeviceId is { } id ? await _devices.GetById(id) : null;

		return device is null
			? new GetDeviceScreenSaverResponse()
			: new GetDeviceScreenSaverResponse
				{ Enabled = device.ScreenSaverEnabled, IdleSeconds = device.ScreenSaverIdleSeconds };
	}
}
