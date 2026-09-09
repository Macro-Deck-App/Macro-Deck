using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>
/// Builds a History Graph widget's <c>widget-config</c> tree from its stored data - see ADR 0050.
/// <c>historyLength</c> has no control here and can never be written by this tree: no node names that key,
/// and the client's structural composition only ever writes the key a dispatched event's own node names
/// (ADR 0050's "an input's id is the widget data key it configures").
/// </summary>
internal static class HistoryGraphWidgetConfigView
{
	private const string _cpuUsage = "system_cpu_usage_percent";
	private const string _cpuName = "system_cpu_name";
	private const string _ramUsedGb = "system_ram_used_gb";
	private const string _ramUsagePercent = "system_ram_usage_percent";
	private const string _gpuUsagePercent = "system_gpu_0_usage_percent";
	private const string _gpuName = "system_gpu_0_name";

	public static UiElement Build(JsonElement data)
	{
		var valueVariable = new UiState<string>(WidgetConfigJson.ReadString(data, "valueVariable") ?? string.Empty);
		var title = new UiState<string>(WidgetConfigJson.ReadString(data, "title") ?? string.Empty);
		var showSubtitle = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showSubtitle") ?? true);
		var subtitle = new UiState<string>(HistoryGraphWidgetData.SubtitleTextOf(
				WidgetConfigJson.ReadString(data, "subtitle"),
				WidgetConfigJson.ReadString(data, "subtitleVariable")) ??
			string.Empty);
		var maxValue = new UiState<double>(NormalizeMaxValue(WidgetConfigJson.ReadDouble(data, "maxValue") ?? 0));
		var minValue = new UiState<double>(WidgetConfigJson.ReadDouble(data, "minValue") ?? 0);
		var accentColor = new UiState<string>(WidgetConfigJson.ReadString(data, "accentColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		// Writes several UiState cells from one interaction, the pattern
		// docs/sdk/ui/concepts/state-and-bindings.md sanctions; a button, so no key holds the preset's name.
		void ApplyPreset(string metric, string presetTitle, string presetSubtitle, double max)
		{
			valueVariable.Value = metric;
			title.Value = presetTitle;
			subtitle.Value = presetSubtitle;
			maxValue.Value = NormalizeMaxValue(max);
			minValue.Value = 0;
		}

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "presets-heading", Text = AppStrings.Widgets.History.Presets() },
					new UiConfigStack
					{
						Key = "presets",
						Direction = "horizontal",
						Children =
						[
							PresetButton("presetCpu",
								AppStrings.Widgets.History.PresetCpu(),
								() => ApplyPreset(_cpuUsage,
									"CPU Load",
									HistoryGraphWidgetData.VariableToken(_cpuName),
									100)),
							PresetButton("presetRamUsed",
								AppStrings.Widgets.History.PresetRamUsed(),
								() => ApplyPreset(_ramUsedGb, "RAM Usage", string.Empty, 0)),
							PresetButton("presetRamPercent",
								AppStrings.Widgets.History.PresetRamPercent(),
								() => ApplyPreset(_ramUsagePercent, "RAM Usage", string.Empty, 100)),
							PresetButton("presetGpu",
								AppStrings.Widgets.History.PresetGpu(),
								() => ApplyPreset(_gpuUsagePercent,
									"GPU Load",
									HistoryGraphWidgetData.VariableToken(_gpuName),
									100)),
						],
					},
					new UiProse { Key = "presets-hint", Text = AppStrings.Widgets.History.PresetsHint() },
					new UiHeading
					{
						Key = "metric-heading", Text = AppStrings.Widgets.History.MetricSection(),
					},
					new UiVariablePickerInput
					{
						Key = "valueVariable",
						Label = AppStrings.Widgets.History.ValueVariable(),
						Placeholder = AppStrings.Widgets.History.ValueVariablePlaceholder(),
						Description = AppStrings.Widgets.History.ValueVariableHint(),
						Binding = Bind.To(valueVariable),
						VariableTypes = UiValue.Of<IReadOnlyList<string>>(["numeric"]),
					},
					new UiStringInput
					{
						Key = "title",
						Label = AppStrings.Widgets.Editor.Title(),
						Placeholder = AppStrings.Widgets.History.TitlePlaceholder(),
						Binding = Bind.To(title),
						LiteralOnly = true,
					},
					new UiBooleanInput
					{
						Key = "showSubtitle",
						Label = AppStrings.Widgets.History.Subtitle(),
						Binding = Bind.To(showSubtitle),
					},
					new UiStringInput
					{
						Key = "subtitle",
						Label = AppStrings.Widgets.History.SubtitleText(),
						Placeholder = AppStrings.Widgets.History.SubtitleTextPlaceholder(),
						Description = AppStrings.Widgets.History.SubtitleTextHint(),
						Binding = Bind.To(subtitle),
						VisibleWhen = new UiVisibleWhen { ParameterName = "showSubtitle", Values = ["true"] },
					},
					// No Min bound, unlike the maximum beside it: a negative floor is the whole point.
					new UiNumberInput
					{
						Key = "minValue",
						Label = AppStrings.Widgets.History.ChartMinimum(),
						Placeholder = AppStrings.Widgets.History.ChartMinimumPlaceholder(),
						Description = AppStrings.Widgets.History.ChartMinimumHint(),
						Binding = Bind.To(minValue),
					},
					new UiNumberInput
					{
						Key = "maxValue",
						Label = AppStrings.Widgets.History.ChartMaximum(),
						Placeholder = AppStrings.Widgets.History.ChartMaximumPlaceholder(),
						Description = AppStrings.Widgets.History.ChartMaximumHint(),
						Binding = Bind.To(maxValue),
						Min = 0,
					},
					new UiColorInput
					{
						Key = "accentColor",
						Label = AppStrings.Widgets.History.AccentColor(),
						Binding = Bind.To(accentColor),
						// Reset returns to unset rather than to a literal hex (issue #896): the chart's real
						// default is the theme accent, which an unset value keeps following as the theme
						// changes - the same "keep an unset colour unset" rule the Action Button label uses.
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiProse { Key = "press-hint", Text = AppStrings.Widgets.History.PressHint() },
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
				],
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows),
		};
	}

	private static UiConfigButton PresetButton(string key, LocalizedString label, Action apply)
		=> new()
		{
			Key = key,
			Label = label,
			Events = [UiEventHandler.On(UiConfigEvents.Activate, apply)],
		};

	/// <summary>Clamps a stored bound to what the <c>maxValue</c> field can hold: <c>0</c> for "auto" (absent,
	/// non-positive or non-finite - the schema's own definition of auto-scaling), since the field is a plain
	/// number with no separate "unset" state.</summary>
	private static double NormalizeMaxValue(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}
