using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateNativeUsbSettingsRequestMessageHandler(INativeUsbManager manager)
	: IUiTransportMessageHandler<UpdateNativeUsbSettingsRequest, UpdateNativeUsbSettingsResponse>
{
	public async ValueTask<UpdateNativeUsbSettingsResponse> Handle(UpdateNativeUsbSettingsRequest request,
		CancellationToken cancellationToken)
	{
		if (request.Enabled is { } enabled)
		{
			await manager.SetEnabledAsync(enabled, cancellationToken);
		}

		await manager.PollAsync(cancellationToken);
		var response = new UpdateNativeUsbSettingsResponse { Success = true };
		return response.Fill<UpdateNativeUsbSettingsResponse>(manager.Status);
	}
}
