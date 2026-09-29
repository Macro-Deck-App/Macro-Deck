using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetDeviceClientSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetDeviceClientSettingsRequest, GetDeviceClientSettingsResponse>
{
	private readonly IDeviceRepository _devices;

	public GetDeviceClientSettingsRequestMessageHandler(IDeviceRepository devices)
	{
		_devices = devices;
	}

	public async ValueTask<GetDeviceClientSettingsResponse> Handle(
		GetDeviceClientSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var device = request.DeviceId is { } id ? await _devices.GetById(id) : null;

		return new GetDeviceClientSettingsResponse { SettingsButtonHidden = device?.SettingsButtonHidden ?? false };
	}
}
