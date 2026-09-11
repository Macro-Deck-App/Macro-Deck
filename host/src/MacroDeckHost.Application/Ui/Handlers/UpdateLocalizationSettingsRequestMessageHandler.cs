using MacroDeck.Localization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateLocalizationSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateLocalizationSettingsRequest, UpdateLocalizationSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IRemotePluginIntegrationRegistrar _registrar;
	private readonly IMediator _mediator;

	public UpdateLocalizationSettingsRequestMessageHandler(IAppPreferenceService preferences,
		IRemotePluginIntegrationRegistrar registrar,
		IMediator mediator)
	{
		_preferences = preferences;
		_registrar = registrar;
		_mediator = mediator;
	}

	public async ValueTask<UpdateLocalizationSettingsResponse> Handle(
		UpdateLocalizationSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var changesLanguage = request.FollowSystem || request.Culture is not null;

		// Validated here, before anything is written: a culture that cannot be used would look saved and
		// then fall back to the default on the next resolve, which is exactly the surprise the setting is
		// supposed to avoid.
		if (changesLanguage && !request.FollowSystem && !LocalizationCultureValidation.IsValid(request.Culture))
		{
			var current = await _preferences.GetLocalization();
			return new UpdateLocalizationSettingsResponse
			{
				Success = false,
				Error = $"'{request.Culture}' is not a recognized culture.",
				Culture = current.Culture,
				FallbackCulture = LocalizationDefaults.Culture,
				FollowSystem = current.FollowSystem,
				TimeFormat = await _preferences.GetTimeFormat()
			};
		}

		if (request.TimeFormat is not null and not (AppPreferenceService.TimeFormatSystem
			or AppPreferenceService.TimeFormat12h
			or AppPreferenceService.TimeFormat24h))
		{
			var current = await _preferences.GetLocalization();
			return new UpdateLocalizationSettingsResponse
			{
				Success = false,
				Error = $"'{request.TimeFormat}' is not a recognized time format.",
				Culture = current.Culture,
				FallbackCulture = LocalizationDefaults.Culture,
				FollowSystem = current.FollowSystem,
				TimeFormat = await _preferences.GetTimeFormat()
			};
		}

		var settings = changesLanguage
			? await _preferences.SetLocalization(request.FollowSystem ? null : request.Culture)
			: await _preferences.GetLocalization();
		var timeFormat = request.TimeFormat is null
			? await _preferences.GetTimeFormat()
			: await _preferences.SetTimeFormat(request.TimeFormat);

		await _mediator.Publish(
				new LocalizationCultureChangedNotification(settings.Culture, LocalizationDefaults.Culture),
				cancellationToken)
			.ConfigureAwait(false);

		if (changesLanguage)
		{
			await _registrar.RefreshLocalizationCatalogsAsync(cancellationToken).ConfigureAwait(false);
		}

		return new UpdateLocalizationSettingsResponse
		{
			Success = true,
			Culture = settings.Culture,
			FallbackCulture = LocalizationDefaults.Culture,
			FollowSystem = settings.FollowSystem,
			TimeFormat = timeFormat
		};
	}
}
