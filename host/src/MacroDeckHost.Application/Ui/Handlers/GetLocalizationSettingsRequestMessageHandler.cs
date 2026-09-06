using MacroDeck.Localization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLocalizationSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetLocalizationSettingsRequest, GetLocalizationSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetLocalizationSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetLocalizationSettingsResponse> Handle(
		GetLocalizationSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetLocalization();

		return new GetLocalizationSettingsResponse
		{
			Culture = settings.Culture,
			FallbackCulture = LocalizationDefaults.Culture,
			FollowSystem = settings.FollowSystem
		};
	}
}
