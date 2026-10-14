using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetDefaultShortPress
{
	string? FlowsSourceFor(WidgetEntity widget, bool fromDevice);

	bool RunsOnDevices(string widgetType);
}

public sealed class WidgetDefaultShortPress : IWidgetDefaultShortPress
{
	private const string WeatherInstanceKey = "instanceId";

	private readonly Func<IWidgetTypeRegistry> _widgetTypes;
	private readonly IIntegrationRegistry _integrations;
	private readonly IWeatherRegistry _weather;
	private readonly ILocalizationResolver _localization;

	// The registry is resolved on use: it depends on the built-in widget providers, which depend on
	// the trigger service that holds this.
	public WidgetDefaultShortPress(
		Func<IWidgetTypeRegistry> widgetTypes,
		IIntegrationRegistry integrations,
		IWeatherRegistry weather,
		ILocalizationResolver localization)
	{
		_widgetTypes = widgetTypes;
		_integrations = integrations;
		_weather = weather;
		_localization = localization;
	}

	public bool RunsOnDevices(string widgetType)
	{
		return _widgetTypes().TryResolve(widgetType, out var entry)
			&& !entry.IsBuiltIn
			&& entry.Descriptor.DefaultShortPressAction is not null;
	}

	public string? FlowsSourceFor(WidgetEntity widget, bool fromDevice)
	{
		ArgumentNullException.ThrowIfNull(widget);

		if (DeclaredDefaultOf(widget) is not var (integrationId, declared, supportsFlows, needsScreen)
			|| fromDevice && needsScreen)
		{
			return null;
		}

		if (supportsFlows && WidgetFlowsJson.SelectedFlowIsRunnable(widget.Data, WidgetTriggerTypes.ShortPress))
		{
			return null;
		}

		if (!_integrations.IsEnabled(integrationId)
			|| _integrations.FindAction(integrationId, declared.ActionId) is not { } action)
		{
			return null;
		}

		var flows = ShortPressFlowJson.Build(integrationId,
			action,
			declared.Parameters ?? new Dictionary<string, string>(),
			_localization,
			null);

		return new JsonObject { ["flows"] = flows }.ToJsonString();
	}

	private DeclaredDefault? DeclaredDefaultOf(WidgetEntity widget)
	{
		if (widget.Type == WidgetTypeIds.Weather)
		{
			return WeatherDetailsDefault(widget);
		}

		// Not DefaultShortPressAction: that one also runs on hardware decks, which have no screen for the dialog.
		if (CalendarWidgetTypes.IsCalendarWidget(widget.Type))
		{
			return new DeclaredDefault(CalendarWidgetTypes.OwnerId,
				new WidgetDefaultAction(CalendarWidgetTypes.ShowDetailsActionId, null),
				true,
				true);
		}

		return _widgetTypes().TryResolve(widget.Type, out var entry)
			&& !entry.IsBuiltIn
			&& entry.Descriptor.DefaultShortPressAction is { } providerDefault
				? new DeclaredDefault(entry.ProviderId, providerDefault, entry.Descriptor.SupportsFlows, false)
				: null;
	}

	private DeclaredDefault? WeatherDetailsDefault(WidgetEntity widget)
	{
		var stationId = ReadString(widget.Data, WeatherInstanceKey) is { Length: > 0 } stored
			? stored
			: _weather.GetInstances() is [var first, ..]
				? first.InstanceId
				: null;

		if (stationId is null
			|| !QualifiedId.TryParse(stationId, out var station)
			|| ProviderOf(station.OwnerId) is not { DetailsAction: { } details })
		{
			return null;
		}

		return new DeclaredDefault(station.OwnerId,
			new WidgetDefaultAction(details.ActionId,
				new Dictionary<string, string> { [details.StationParameter] = stationId }),
			true,
			true);
	}

	private IWeatherProvider? ProviderOf(string integrationId)
	{
		return _integrations.Integrations.FirstOrDefault(integration =>
			string.Equals(integration.Id, integrationId, StringComparison.Ordinal)) as IWeatherProvider;
	}

	private static string? ReadString(string? data, string key)
	{
		if (string.IsNullOrWhiteSpace(data))
		{
			return null;
		}

		try
		{
			return JsonNode.Parse(data) is JsonObject root
				&& root[key] is JsonValue value
				&& value.TryGetValue<string>(out var text)
					? text
					: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private sealed record DeclaredDefault(
		string IntegrationId,
		WidgetDefaultAction Action,
		bool SupportsFlows,
		bool NeedsScreen);
}
