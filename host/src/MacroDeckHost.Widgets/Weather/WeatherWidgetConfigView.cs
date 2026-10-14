using System.Globalization;
using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Weather;

/// <summary>Builds a Weather widget's <c>widget-config</c> tree from its stored data - see ADR 0050.</summary>
internal static class WeatherWidgetConfigView
{
	public static UiElement Build(JsonElement data, IWeatherRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		var instanceId = new UiState<string>(WidgetConfigJson.ReadString(data, "instanceId") ?? string.Empty);
		var showIcon = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showIcon") ?? true);
		var showTemperature = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showTemperature") ?? true);
		var showCondition = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showCondition") ?? true);
		// Must match WeatherWidgetData.Parse: the editor recomposes every bound key on save.
		var showLocation = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showLocation")
			?? WidgetConfigJson.ReadBool(data, "showCondition") ?? true);
		var style = new UiState<string>(WeatherWidgetData.Parse(data).Style);
		var showForecast = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showForecast") ?? true);
		var animateIcon = new UiState<bool>(WidgetConfigJson.ReadBool(data, "animateIcon") ?? true);
		var forecastDays = new UiState<double>(
			Math.Clamp(WidgetConfigJson.ReadDouble(data, "forecastDays") ?? 5, 1, 7));

		var backgroundColor =
			new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		var instanceOptions = new UiOptionsState(UiOptionSource.From((_, _)
			=> Task.FromResult<IReadOnlyList<UiOption>>(InstanceOptions(registry))));
		instanceOptions.Reload();

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading
					{
						Key = "location-heading", Text = AppStrings.Widgets.Weather.LocationSection()
					},
					LocationPicker(registry, instanceId, instanceOptions),
					new UiHeading { Key = "style-heading", Text = AppStrings.Widgets.Editor.Style() },
					new UiChoiceInput
					{
						Key = WeatherWidgetData.StyleKey,
						Label = AppStrings.Widgets.Editor.Style(),
						HideLabel = true,
						Segmented = true,
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(WeatherWidgetData.StyleForecast, AppStrings.Widgets.Weather.Forecast()),
							UiOption.Of(WeatherWidgetData.StyleCompact, AppStrings.Widgets.Weather.Styles.Compact())
						])
					},
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiBooleanInput
					{
						Key = "showIcon",
						Label = AppStrings.Widgets.Weather.ConditionIcon(),
						Binding = Bind.To(showIcon)
					},
					new UiBooleanInput
					{
						Key = "showTemperature",
						Label = AppStrings.Widgets.Weather.Temperature(),
						Binding = Bind.To(showTemperature)
					},
					new UiBooleanInput
					{
						Key = "showCondition",
						Label = AppStrings.Widgets.Weather.ConditionText(),
						Binding = Bind.To(showCondition)
					},
					new UiBooleanInput
					{
						Key = "showLocation",
						Label = AppStrings.Widgets.Weather.LocationName(),
						Binding = Bind.To(showLocation)
					},
					new UiBooleanInput
					{
						Key = "showForecast",
						Label = AppStrings.Widgets.Weather.Forecast(),
						Binding = Bind.To(showForecast),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = WeatherWidgetData.StyleKey,
							Values = [WeatherWidgetData.StyleForecast],
							SiblingValue = () => style.Value
						}
					},
					new UiBooleanInput
					{
						Key = "animateIcon",
						Label = AppStrings.Widgets.Weather.AnimateIcon(),
						Binding = Bind.To(animateIcon),
						VisibleWhen = new UiVisibleWhen { ParameterName = "showIcon", Values = ["true"] }
					},
					new UiWhen
					{
						Key = "forecastDaysGate",
						Condition = () => style.Value == WeatherWidgetData.StyleForecast,
						Content = () => new UiNumberInput
						{
							Key = "forecastDays",
							Label = AppStrings.Widgets.Weather.ForecastDays(),
							Binding = Bind.To(forecastDays),
							Min = 1,
							Max = 7,
							Step = 1,
							// A closed range of seven, each of which reads as a phrase rather than a digit -
							// picked from a list, the way the original form offered it, not typed into a box.
							Options = UiValue.Of(_forecastDayOptions),
							VisibleWhen = new UiVisibleWhen { ParameterName = "showForecast", Values = ["true"] }
						}
					},
					new UiProse
					{
						Key = "live-update-hint", Text = AppStrings.Widgets.Weather.LiveUpdateHint()
					},
					new UiHeading { Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance() },
					WidgetConfigFragments.Background(backgroundColor),
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, false)
				]
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows)
		};
	}

	private static readonly IReadOnlyList<UiOption> _forecastDayOptions =
	[
		.. Enumerable.Range(1, 7)
			.Select(days => UiOption.Of(days.ToString(CultureInfo.InvariantCulture),
				AppStrings.Widgets.Weather.ForecastDaysCount(days)))
	];

	/// <summary>The location picker, carrying the "nothing set up yet" sentence only while there is
	/// nothing to pick - with a location configured the sentence is noise. Set rather than blanked: a
	/// description the DSL never saw is absent, where an empty one is a text with nothing in it.</summary>
	private static UiDynamicChoiceInput LocationPicker(
		IWeatherRegistry registry,
		UiState<string> instanceId,
		UiOptionsState instanceOptions)
	{
		var picker = new UiDynamicChoiceInput
		{
			Key = "instanceId",
			Label = AppStrings.Widgets.Weather.WeatherLocation(),
			Binding = Bind.To(instanceId),
			OptionsState = instanceOptions
		};

		return registry.GetInstances().Count == 0
			? picker with { Description = AppStrings.Widgets.Weather.NoLocationsHint() }
			: picker;
	}

	private static List<UiOption> InstanceOptions(IWeatherRegistry registry)
	{
		// UiOption.Of rejects an empty value, so the "first available" sentinel - the empty instanceId
		// itself - is composed directly rather than through the factory.
		var options = new List<UiOption>
		{
			new() { Value = string.Empty, Label = AppStrings.Widgets.Weather.FirstAvailable() }
		};

		options.AddRange(registry.GetInstances()
			.Select(instance => UiOption.Of(instance.InstanceId, instance.DisplayName)));

		return options;
	}
}
