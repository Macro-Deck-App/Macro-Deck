using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Rendering;
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
	private readonly IFontCatalog _fonts;

	public UpdateAppearanceSettingsRequestMessageHandler(IAppPreferenceService service,
		IMediator mediator,
		IFontCatalog fonts)
	{
		_service = service;
		_mediator = mediator;
		_fonts = fonts;
	}

	public async ValueTask<UpdateAppearanceSettingsResponse> Handle(
		UpdateAppearanceSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetAppearance(request.ThemeMode,
			request.AccentColor,
			NormalizeFontFamily(request.FontFamily));

		await _mediator.Publish(
			new AppearanceChangedNotification(settings.ThemeMode, settings.AccentColor, settings.FontFamily),
			cancellationToken);

		return new UpdateAppearanceSettingsResponse
		{
			ThemeMode = settings.ThemeMode,
			AccentColor = settings.AccentColor,
			FontFamily = settings.FontFamily
		};
	}

	private string? NormalizeFontFamily(string? fontFamily)
	{
		var family = fontFamily?.Trim();
		if (string.IsNullOrEmpty(family))
		{
			return family;
		}

		return _fonts.GetFaces()
			.Any(face => face.RemoteRenderable && string.Equals(face.Family, family, StringComparison.Ordinal))
			? family
			: string.Empty;
	}
}
