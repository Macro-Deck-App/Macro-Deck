using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RefreshNativeUsbSettingsRequestMessageHandler(INativeUsbManager manager)
	: IUiTransportMessageHandler<RefreshNativeUsbSettingsRequest, GetNativeUsbSettingsResponse>
{
	public async ValueTask<GetNativeUsbSettingsResponse> Handle(RefreshNativeUsbSettingsRequest request,
		CancellationToken cancellationToken)
	{
		await manager.PollAsync(cancellationToken);
		return new GetNativeUsbSettingsResponse().Fill<GetNativeUsbSettingsResponse>(manager.Status);
	}
}
