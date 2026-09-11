using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetAppearanceSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetAppearanceSettingsRequest, GetAppearanceSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetAppearanceSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetAppearanceSettingsResponse> Handle(
		GetAppearanceSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetAppearance();

		return new GetAppearanceSettingsResponse
		{
			ThemeMode = settings.ThemeMode,
			AccentColor = settings.AccentColor,
			FontFamily = settings.FontFamily
		};
	}
}
