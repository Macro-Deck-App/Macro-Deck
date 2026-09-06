using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLockScreenSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetLockScreenSettingsRequest, GetLockScreenSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetLockScreenSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetLockScreenSettingsResponse> Handle(
		GetLockScreenSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetLockScreen();

		return new GetLockScreenSettingsResponse { Enabled = settings.Enabled };
	}
}
