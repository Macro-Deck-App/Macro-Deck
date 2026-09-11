using MacroDeck.Localization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLocalizationRequestMessageHandler
	: IUiTransportMessageHandler<GetLocalizationRequest, GetLocalizationResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationCatalogRegistry _catalogs;
	private readonly ILocalizationResolver _resolver;
	private readonly TimeFormatResolver _timeFormat;

	public GetLocalizationRequestMessageHandler(IAppPreferenceService preferences,
		ILocalizationCatalogRegistry catalogs,
		ILocalizationResolver resolver,
		TimeFormatResolver timeFormat)
	{
		_preferences = preferences;
		_catalogs = catalogs;
		_resolver = resolver;
		_timeFormat = timeFormat;
	}

	public async ValueTask<GetLocalizationResponse> Handle(
		GetLocalizationRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _preferences.GetLocalization();

		var translations = new Dictionary<string, string>(StringComparer.Ordinal);
		var availableCultures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var scope in _catalogs.Scopes)
		{
			var catalog = _catalogs.Find(scope);
			if (catalog is null)
			{
				continue;
			}

			foreach (var culture in catalog.Cultures)
			{
				availableCultures.Add(culture);
			}

			// The union across cultures, not just the active one: a key only the default language carries
			// still has to reach the client, which resolves it through the same fallback chain the host
			// applied here.
			var keys = new HashSet<string>(StringComparer.Ordinal);

			foreach (var culture in catalog.Cultures)
			{
				foreach (var key in catalog.KeysOf(culture))
				{
					keys.Add(key);
				}
			}

			foreach (var key in keys)
			{
				translations[$"{scope}:{key}"] =
					_resolver.Resolve(new LocalizedString(new LocalizationKey(scope, key)), settings.Culture);
			}
		}

		return new GetLocalizationResponse
		{
			Culture = settings.Culture,
			FallbackCulture = LocalizationDefaults.Culture,
			Translations = translations,
			AvailableCultures = [.. availableCultures],
			FollowSystem = settings.FollowSystem,
			TimeFormat = await _preferences.GetTimeFormat(),
			HourCycle = await _timeFormat.ResolveHourCycle()
		};
	}
}
