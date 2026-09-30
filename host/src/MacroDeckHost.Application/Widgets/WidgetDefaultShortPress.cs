using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations;
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
	private const string WeatherIntegrationId = "app.macro-deck.weather";

	private const string WeatherDetailsActionId = "show-details";

	private const string WeatherInstanceKey = "instanceId";

	private readonly Func<IWidgetTypeRegistry> _widgetTypes;
	private readonly IIntegrationRegistry _integrations;
	private readonly ILocalizationResolver _localization;

	// The registry is resolved on use: it depends on the built-in widget providers, which depend on
	// the trigger service that holds this.
	public WidgetDefaultShortPress(
		Func<IWidgetTypeRegistry> widgetTypes,
		IIntegrationRegistry integrations,
		ILocalizationResolver localization)
	{
		_widgetTypes = widgetTypes;
		_integrations = integrations;
		_localization = localization;
	}

	public bool RunsOnDevices(string widgetType)
		=> _widgetTypes().TryResolve(widgetType, out var entry) &&
			!entry.IsBuiltIn &&
			entry.Descriptor.DefaultShortPressAction is not null;

	public string? FlowsSourceFor(WidgetEntity widget, bool fromDevice)
	{
		ArgumentNullException.ThrowIfNull(widget);

		if (DeclaredDefaultOf(widget) is not (var integrationId, var declared, var supportsFlows, var needsScreen) ||
			(fromDevice && needsScreen))
		{
			return null;
		}

		if (supportsFlows && WidgetFlowsJson.SelectedFlowIsRunnable(widget.Data, WidgetTriggerTypes.ShortPress))
		{
			return null;
		}

		if (!_integrations.IsEnabled(integrationId) ||
			_integrations.FindAction(integrationId, declared.ActionId) is not { } action)
		{
			return null;
		}

		var flows = ShortPressFlowJson.Build(integrationId,
			action,
			declared.Parameters ?? new Dictionary<string, string>(),
			_localization,
			culture: null);

		return new JsonObject { ["flows"] = flows }.ToJsonString();
	}

	private DeclaredDefault? DeclaredDefaultOf(WidgetEntity widget)
	{
		if (widget.Type == WidgetTypeIds.Weather)
		{
			var instanceId = ReadString(widget.Data, WeatherInstanceKey);
			return new DeclaredDefault(WeatherIntegrationId,
				new WidgetDefaultAction(WeatherDetailsActionId,
					string.IsNullOrEmpty(instanceId)
						? null
						: new Dictionary<string, string> { [WeatherInstanceKey] = instanceId }),
				SupportsFlows: true,
				NeedsScreen: true);
		}

		return _widgetTypes().TryResolve(widget.Type, out var entry) &&
			!entry.IsBuiltIn &&
			entry.Descriptor.DefaultShortPressAction is { } providerDefault
				? new DeclaredDefault(entry.ProviderId, providerDefault, entry.Descriptor.SupportsFlows, NeedsScreen: false)
				: null;
	}

	private static string? ReadString(string? data, string key)
	{
		if (string.IsNullOrWhiteSpace(data))
		{
			return null;
		}

		try
		{
			return JsonNode.Parse(data) is JsonObject root &&
				root[key] is JsonValue value &&
				value.TryGetValue<string>(out var text)
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
