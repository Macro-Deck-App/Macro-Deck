using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Ping.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping.Widget;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal static class PingWidgetConfigView
{
	public static UiElement Build(JsonElement data, IReadOnlyList<PingSnapshot> targets)
	{
		ArgumentNullException.ThrowIfNull(targets);

		var options = PingWidgetSettings.Parse(data);
		var target = new UiState<string>(options.TargetId ?? string.Empty);
		var displayName = new UiState<string>(options.DisplayName ?? string.Empty);
		var showLatency = new UiState<bool>(options.ShowLatency);
		var showPacketLoss = new UiState<bool>(options.ShowPacketLoss);
		var showHistory = new UiState<bool>(options.ShowHistory);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "heading", Text = Strings.Name() },
					new UiChoiceInput
					{
						Key = PingWidgetType.TargetKey,
						Label = Strings.Config.Target(),
						Description = Strings.Config.TargetDescription(),
						Placeholder = Strings.Config.FirstTarget(),
						Binding = Bind.To(target),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							.. targets.Select(candidate
								=> UiOption.Of(candidate.EntryId.ToString("D"), candidate.Title))
						])
					},
					new UiStringInput
					{
						Key = PingWidgetType.DisplayNameKey,
						Label = Strings.Config.DisplayName(),
						Placeholder = Strings.Config.DisplayNamePlaceholder(),
						Binding = Bind.To(displayName)
					},
					new UiBooleanInput
					{
						Key = PingWidgetType.ShowLatencyKey,
						Label = Strings.Config.ShowLatency(),
						Binding = Bind.To(showLatency)
					},
					new UiBooleanInput
					{
						Key = PingWidgetType.ShowPacketLossKey,
						Label = Strings.Config.ShowPacketLoss(),
						Binding = Bind.To(showPacketLoss)
					},
					new UiBooleanInput
					{
						Key = PingWidgetType.ShowHistoryKey,
						Label = Strings.Config.ShowHistory(),
						Binding = Bind.To(showHistory)
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border
						| UiWidgetAppearanceFields.BackgroundColor
						| UiWidgetAppearanceFields.TransparentBackground)
				]
			}
		};
	}
}
