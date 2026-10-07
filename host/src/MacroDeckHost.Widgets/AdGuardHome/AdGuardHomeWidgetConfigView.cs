using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Widgets.Configuration;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.AdGuardHome;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal static class AdGuardHomeWidgetConfigView
{
	private static readonly IReadOnlyList<string> _withControls =
		[AdGuardHomeWidgetType.ControlView, AdGuardHomeWidgetType.OverviewView];

	private static readonly IReadOnlyList<string> _withStatistics =
		[AdGuardHomeWidgetType.StatisticsView, AdGuardHomeWidgetType.OverviewView];

	public static UiElement Build(JsonElement data, IReadOnlyList<AdGuardHomeSnapshot> instances)
	{
		ArgumentNullException.ThrowIfNull(instances);

		var options = AdGuardHomeWidgetSettings.Parse(data);
		var instance = new UiState<string>(options.InstanceId ?? string.Empty);
		var displayName = new UiState<string>(options.DisplayName ?? string.Empty);
		var view = new UiState<string>(options.View);
		var statistics = new UiState<IReadOnlyList<string>>(options.Statistics);
		var durations = new UiState<IReadOnlyList<string>>(options.Durations);
		var showVersion = new UiState<bool>(options.ShowVersion);
		var showStatus = new UiState<bool>(options.ShowStatus);

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
						Key = AdGuardHomeWidgetType.InstanceKey,
						Label = Strings.Config.Instance(),
						Description = Strings.Config.InstanceDescription(),
						Placeholder = Strings.Config.FirstInstance(),
						Binding = Bind.To(instance),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. instances.Select(candidate => UiOption.Of(candidate.EntryId, candidate.Title)),
						]),
					},
					new UiStringInput
					{
						Key = AdGuardHomeWidgetType.DisplayNameKey,
						Label = Strings.Config.DisplayName(),
						Placeholder = Strings.Config.DisplayNamePlaceholder(),
						Binding = Bind.To(displayName),
					},
					new UiChoiceInput
					{
						Key = AdGuardHomeWidgetType.ViewKey,
						Label = Strings.Config.View(),
						Description = Strings.Config.ViewDescription(),
						Binding = Bind.To(view),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(AdGuardHomeWidgetType.ControlView, Strings.Config.ViewControl()),
							UiOption.Of(AdGuardHomeWidgetType.StatisticsView, Strings.Config.ViewStatistics()),
							UiOption.Of(AdGuardHomeWidgetType.OverviewView, Strings.Config.ViewOverview()),
						]),
					},
					new UiMultiSelectInput
					{
						Key = AdGuardHomeWidgetType.DurationsKey,
						Label = Strings.Config.Durations(),
						Description = Strings.Config.DurationsDescription(),
						Binding = Bind.To(durations),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. AdGuardHomeWidgetType.DurationIds.Select(id => UiOption.Of(id,
								id == AdGuardHomeWidgetType.IndefiniteDuration
									? Strings.DisableIndefinitely()
									: AdGuardHomeDurationText.Label(id))),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = AdGuardHomeWidgetType.ViewKey,
							Values = _withControls,
							SiblingValue = () => view.Value,
						},
					},
					new UiMultiSelectInput
					{
						Key = AdGuardHomeWidgetType.StatisticsKey,
						Label = Strings.Config.Statistics(),
						Description = Strings.Config.StatisticsDescription(),
						Binding = Bind.To(statistics),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. AdGuardHomeWidgetType.Statistics.Select(id =>
								UiOption.Of(id, AdGuardHomeWidgetView.StatisticLabel(id))),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = AdGuardHomeWidgetType.ViewKey,
							Values = _withStatistics,
							SiblingValue = () => view.Value,
						},
					},
					new UiBooleanInput
					{
						Key = AdGuardHomeWidgetType.ShowVersionKey,
						Label = Strings.Config.ShowVersion(),
						Binding = Bind.To(showVersion),
					},
					new UiBooleanInput
					{
						Key = AdGuardHomeWidgetType.ShowStatusKey,
						Label = Strings.Config.ShowStatus(),
						Binding = Bind.To(showStatus),
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border | UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.TransparentBackground),
				],
			},
		};
	}
}
