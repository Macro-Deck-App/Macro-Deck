using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Clock;

/// <summary>Builds a Clock widget's <c>widget-config</c> tree from its stored data - see ADR 0050 and
/// <c>docs/src/content/docs/ui/views/widget-configuration.md</c>.</summary>
internal static class ClockWidgetConfigView
{
	public static UiElement Build(JsonElement data)
	{
		var style = new UiState<string>(WidgetConfigJson.ReadString(data, "style") ?? "digital");
		var timeZone = new UiState<string>(WidgetConfigJson.ReadString(data, "timeZone") ?? string.Empty);
		var showLabel = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showLabel") ?? false);
		var label = new UiState<string>(WidgetConfigJson.ReadString(data, "label") ?? string.Empty);
		var showSeconds = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showSeconds") ?? true);
		var showDate = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showDate") ?? true);
		var showOffset = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showOffset") ?? false);
		var hourCycle = new UiState<string>(WidgetConfigJson.ReadString(data, "hourCycle") ?? "auto");
		var leadingZero = new UiState<bool>(WidgetConfigJson.ReadBool(data, "leadingZero") ?? true);
		var dateFormat = new UiState<string>(WidgetConfigJson.ReadString(data, "dateFormat") ?? "default");
		var datePosition = new UiState<string>(WidgetConfigJson.ReadString(data, "datePosition") ?? "below");
		var backgroundColor = new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);
		var textColor = new UiState<string>(WidgetConfigJson.ReadString(data, "textColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		var timeZoneOptions = new UiOptionsState(UiOptionSource.From((_, _)
			=> Task.FromResult<IReadOnlyList<UiOption>>(ClockTimeZones.Options())));
		timeZoneOptions.Reload();

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "clock-heading", Text = AppStrings.Widgets.Clock.SectionHeading() },
					new UiChoiceInput
					{
						Key = "style",
						Label = AppStrings.Widgets.Clock.Style(),
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of("digital", AppStrings.Widgets.Clock.StyleDigital()),
							UiOption.Of("analog", AppStrings.Widgets.Clock.StyleAnalog()),
						]),
					},
					new UiDynamicChoiceInput
					{
						Key = "timeZone",
						Label = AppStrings.Widgets.Clock.TimeZone(),
						Placeholder = AppStrings.Widgets.Clock.TimeZonePlaceholder(),
						Description = AppStrings.Widgets.Clock.TimeZoneHint(),
						Binding = Bind.To(timeZone),
						OptionsState = timeZoneOptions,
						AllowsCustomValue = true,
					},
					new UiHeading
					{
						Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance(),
					},
					new UiColorInput
					{
						Key = "backgroundColor",
						Label = AppStrings.Widgets.Editor.BackgroundColor(),
						Binding = Bind.To(backgroundColor),
						SupportsReset = true,
					},
					new UiColorInput
					{
						Key = "textColor",
						Label = AppStrings.Widgets.Clock.TextColor(),
						Binding = Bind.To(textColor),
						SupportsReset = true,
					},
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiChoiceInput
					{
						Key = "hourCycle",
						Label = AppStrings.Widgets.Clock.HourCycle(),
						Binding = Bind.To(hourCycle),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of("auto", AppStrings.Widgets.Clock.HourCycleAuto()),
							UiOption.Of("12h", "12"),
							UiOption.Of("24h", "24"),
						]),
					},
					new UiChoiceInput
					{
						Key = "dateFormat",
						Label = AppStrings.Widgets.Clock.DateFormat(),
						Binding = Bind.To(dateFormat),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of("default", AppStrings.Widgets.Clock.DateFormatDefault()),
							UiOption.Of("day-first", AppStrings.Widgets.Clock.DateFormatDayFirst()),
							UiOption.Of("month-first", AppStrings.Widgets.Clock.DateFormatMonthFirst()),
							UiOption.Of("iso", AppStrings.Widgets.Clock.DateFormatIso()),
							UiOption.Of("long", AppStrings.Widgets.Clock.DateFormatLong()),
						]),
						VisibleWhen = new UiVisibleWhen { ParameterName = "showDate", Values = ["true"] },
					},
					new UiChoiceInput
					{
						Key = "datePosition",
						Label = AppStrings.Widgets.Clock.DatePosition(),
						Binding = Bind.To(datePosition),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of("below", AppStrings.Widgets.Clock.DatePositionBelow()),
							UiOption.Of("above", AppStrings.Widgets.Clock.DatePositionAbove()),
							UiOption.Of("left", AppStrings.Widgets.Clock.DatePositionLeft()),
							UiOption.Of("right", AppStrings.Widgets.Clock.DatePositionRight()),
						]),
						VisibleWhen = new UiVisibleWhen { ParameterName = "showDate", Values = ["true"] },
					},
					new UiBooleanInput
					{
						Key = "showSeconds",
						Label = AppStrings.Widgets.Clock.Seconds(),
						Binding = Bind.To(showSeconds),
					},
					new UiBooleanInput
					{
						Key = "showDate",
						Label = AppStrings.Widgets.Clock.Date(),
						Binding = Bind.To(showDate),
					},
					new UiBooleanInput
					{
						Key = "leadingZero",
						Label = AppStrings.Widgets.Clock.LeadingZero(),
						// Meaningless while the face is the viewing device's choice, which is what the
						// hint says rather than hiding a control whose stored value still applies once
						// a face is chosen.
						Description = AppStrings.Widgets.Clock.LeadingZeroAutoHint(),
						Binding = Bind.To(leadingZero),
					},
					// Structural rather than VisibleWhen: the row is offered exactly while a zone is
					// named, and "any value but the empty one" is not something a value list can say.
					new UiWhen
					{
						Key = "offsetZoned",
						Condition = () => !string.IsNullOrEmpty(timeZone.Value),
						Content = () => new UiBooleanInput
						{
							Key = "showOffset",
							Label = AppStrings.Widgets.Clock.Offset(),
							Binding = Bind.To(showOffset),
						},
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
						Placeholder = AppStrings.Widgets.Clock.LabelPlaceholderExample(),
						Description = AppStrings.Widgets.Clock.CaptionHint(),
						Binding = Bind.To(label),
						HideLabel = true,
						VisibleWhen = new UiVisibleWhen { ParameterName = "showLabel", Values = ["true"] },
					},
					new UiProse { Key = "update-hint", Text = AppStrings.Widgets.Clock.UpdateHint() },
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
				],
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows),
		};
	}
}
