using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateAppearanceSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateAppearanceSettingsRequest, UpdateAppearanceSettingsResponse>
{
	private readonly IAppPreferenceService _service;
	private readonly IMediator _mediator;

	public UpdateAppearanceSettingsRequestMessageHandler(IAppPreferenceService service, IMediator mediator)
	{
		_service = service;
		_mediator = mediator;
	}

	public async ValueTask<UpdateAppearanceSettingsResponse> Handle(
		UpdateAppearanceSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetAppearance(request.ThemeMode, request.AccentColor);

		await _mediator.Publish(new AppearanceChangedNotification(settings.ThemeMode, settings.AccentColor),
			cancellationToken);

		return new UpdateAppearanceSettingsResponse
		{
			ThemeMode = settings.ThemeMode,
			AccentColor = settings.AccentColor
		};
	}
}
