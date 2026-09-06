using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetDeveloperSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetDeveloperSettingsRequest, GetDeveloperSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetDeveloperSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetDeveloperSettingsResponse> Handle(
		GetDeveloperSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetDeveloper();

		return new GetDeveloperSettingsResponse { Enabled = settings.Enabled };
	}
}
