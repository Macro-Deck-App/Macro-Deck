using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetNativeUsbSettingsRequestMessageHandler(INativeUsbManager manager)
	: IUiTransportMessageHandler<GetNativeUsbSettingsRequest, GetNativeUsbSettingsResponse>
{
	public ValueTask<GetNativeUsbSettingsResponse> Handle(GetNativeUsbSettingsRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetNativeUsbSettingsResponse().Fill<GetNativeUsbSettingsResponse>(manager.Status));
}
