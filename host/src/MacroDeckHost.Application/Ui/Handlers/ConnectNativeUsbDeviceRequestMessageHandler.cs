using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ConnectNativeUsbDeviceRequestMessageHandler(INativeUsbManager manager)
	: IUiTransportMessageHandler<ConnectNativeUsbDeviceRequest, ConnectNativeUsbDeviceResponse>
{
	public async ValueTask<ConnectNativeUsbDeviceResponse> Handle(ConnectNativeUsbDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await manager.PickAsync(request.Id ?? string.Empty, cancellationToken);
		var response = new ConnectNativeUsbDeviceResponse
		{
			Success = result == NativeUsbPickResult.Picked,
			ErrorCode = result == NativeUsbPickResult.Picked ? null : result.ToString()
		};
		return response.Fill<ConnectNativeUsbDeviceResponse>(manager.Status);
	}
}
