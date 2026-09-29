using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Timers;

internal static class TimerWidgetConfigView
{
	public static UiElement Build(JsonElement data, string widgetTypeId)
	{
		var isCountdown = widgetTypeId == WidgetTypeIds.Countdown;

		var mode = new UiState<string>(WidgetConfigJson.ReadString(data, "mode") ?? "fixed");
		var hasDuration = ReadNumber(data, "durationHours") is not null ||
			ReadNumber(data, "durationMinutes") is not null ||
			ReadNumber(data, "durationSeconds") is not null;
		var durationHours = new UiState<double>(ReadNumber(data, "durationHours") ?? 0);
		var durationMinutes = new UiState<double>(ReadNumber(data, "durationMinutes") ??
			(hasDuration ? 0 : TimerWidgetConfig.DefaultDurationSeconds / 60));
		var durationSeconds = new UiState<double>(ReadNumber(data, "durationSeconds") ?? 0);
		var showLabel = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showLabel") ?? false);
		var label = new UiState<string>(WidgetConfigJson.ReadString(data, "label") ?? string.Empty);
		var accentColor = new UiState<string>(WidgetConfigJson.ReadString(data, "accentColor") ?? string.Empty);
		var backgroundColor = new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		var children = new List<UiElement>();

		if (isCountdown)
		{
			children.AddRange(
			[
				new UiHeading { Key = "countdown-heading", Text = AppStrings.WebClient.Widgets.Countdown.Name() },
				new UiChoiceInput
				{
					Key = "mode",
					Label = AppStrings.Widgets.Countdown.Mode(),
					Description = AppStrings.Widgets.Countdown.ModeHint(),
					Binding = Bind.To(mode),
					Options = UiValue.Of<IReadOnlyList<UiOption>>([
						UiOption.Of("fixed", AppStrings.Widgets.Countdown.ModeFixed()),
						UiOption.Of(TimerWidgetConfig.AskMode, AppStrings.Widgets.Countdown.ModeAsk()),
					]),
				},
				new UiWhen
				{
					Key = "durationGate",
					Condition = () => mode.Value != TimerWidgetConfig.AskMode,
					Content = () => new UiConfigStack
					{
						Key = "duration",
						Direction = "horizontal",
						Wrap = false,
						Children =
						[
							DurationPart("durationHours", AppStrings.Widgets.Countdown.Dialog.Hours(), durationHours, 23),
							DurationPart("durationMinutes", AppStrings.Widgets.Countdown.Dialog.Minutes(), durationMinutes, 59),
							DurationPart("durationSeconds", AppStrings.Widgets.Clock.Seconds(), durationSeconds, 59),
						],
					},
				},
			]);
		}

		children.AddRange(
		[
			new UiHeading { Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance() },
			new UiColorInput
			{
				Key = "accentColor",
				Label = AppStrings.Widgets.History.AccentColor(),
				Binding = Bind.To(accentColor),
				SupportsReset = true,
			},
			new UiColorInput
			{
				Key = "backgroundColor",
				Label = AppStrings.Widgets.Editor.BackgroundColor(),
				Binding = Bind.To(backgroundColor),
				SupportsReset = true,
			},
			new UiBooleanInput
			{
				Key = "showLabel",
				Label = AppStrings.Widgets.Editor.Label(),
				Binding = Bind.To(showLabel),
			},
			new UiStringInput
			{
				Key = "label",
				Placeholder = AppStrings.Widgets.Timer.LabelPlaceholderExample(),
				Binding = Bind.To(label),
				HideLabel = true,
				VisibleWhen = new UiVisibleWhen { ParameterName = "showLabel", Values = ["true"] },
			},
			new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
			WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
		]);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties { Key = "properties", Children = children },
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children =
				[
					new UiActionsListEditor
					{
						Key = "flows",
						Binding = Bind.To(flows),
						CanRun = true,
						Triggers = UiValue.Of(Triggers(isCountdown)),
					},
				],
			},
		};
	}

	public static IReadOnlyList<string> Triggers(bool isCountdown)
		=> isCountdown
			?
			[
				WidgetTriggerTypes.CountdownFinished, WidgetTriggerTypes.CountdownStarted, WidgetTriggerTypes.CountdownPaused,
				WidgetTriggerTypes.CountdownReset, WidgetTriggerTypes.CountdownDismissed,
			]
			: [WidgetTriggerTypes.StopwatchStarted, WidgetTriggerTypes.StopwatchPaused, WidgetTriggerTypes.StopwatchReset];

	private static UiNumberInput DurationPart(string key, UiText label, UiState<double> value, double max)
		=> new()
		{
			Key = key,
			Label = label,
			Binding = Bind.To(value),
			Min = 0,
			Max = max,
			Step = 1,
			RowWeight = 1,
		};

	private static double? ReadNumber(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number
				? value.GetDouble()
				: null;
}
