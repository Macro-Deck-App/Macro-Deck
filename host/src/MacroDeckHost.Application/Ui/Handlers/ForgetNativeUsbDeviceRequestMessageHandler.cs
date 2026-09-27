using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ForgetNativeUsbDeviceRequestMessageHandler(INativeUsbManager manager)
	: IUiTransportMessageHandler<ForgetNativeUsbDeviceRequest, ForgetNativeUsbDeviceResponse>
{
	public async ValueTask<ForgetNativeUsbDeviceResponse> Handle(ForgetNativeUsbDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var response = new ForgetNativeUsbDeviceResponse
		{
			Success = await manager.ForgetAsync(request.Serial ?? string.Empty, cancellationToken)
		};
		return response.Fill<ForgetNativeUsbDeviceResponse>(manager.Status);
	}
}
