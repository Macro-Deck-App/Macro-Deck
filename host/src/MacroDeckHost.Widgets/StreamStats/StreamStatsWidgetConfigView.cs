using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.StreamStats.Widget;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsWidgetConfigView
{
	public static UiElement Build(StreamPlatform platform, JsonElement data, IReadOnlyList<StreamStatsAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(accounts);

		var account = new UiState<string>(WidgetConfigJson.ReadString(data, StreamStatsWidgetType.AccountKey) ??
			string.Empty);
		var options = StreamStatsWidgetSettings.Options(platform, data);
		var style = new UiState<string>(options.Style);
		var metric = new UiState<string>(options.Metric);
		var tiles = new UiState<IReadOnlyList<string>>(options.Tiles);
		var details = new UiState<IReadOnlyList<string>>(options.Details);
		var showThumbnail = new UiState<bool>(options.ShowThumbnail);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "stats-heading", Text = platform.Stats.Name() },
					new UiChoiceInput
					{
						Key = StreamStatsWidgetType.AccountKey,
						Label = platform.AccountLabel(),
						Description = platform.Stats.AccountDescription(),
						Placeholder = AppStrings.Integrations.StreamChat.Widget.FirstAccount(),
						Binding = Bind.To(account),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. accounts.Select(candidate => UiOption.Of(candidate.AccountId, candidate.Label)),
						]),
					},
					new UiChoiceInput
					{
						Key = StreamStatsWidgetType.StyleKey,
						Label = Strings.Style(),
						Description = Strings.StyleDescription(),
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(StreamStatsStyles.Overview, Strings.StyleOverview()),
							UiOption.Of(StreamStatsStyles.StatsRow, Strings.StyleStatsRow()),
							UiOption.Of(StreamStatsStyles.LiveRow, Strings.StyleLiveRow()),
							UiOption.Of(StreamStatsStyles.ValueGraph, Strings.StyleValueGraph()),
							UiOption.Of(StreamStatsStyles.Value, Strings.StyleValue()),
						]),
					},
					new UiChoiceInput
					{
						Key = StreamStatsWidgetType.MetricKey,
						Label = Strings.Metric(),
						Description = Strings.MetricDescription(),
						Binding = Bind.To(metric),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. platform.Stats.Metrics.Select(candidate => UiOption.Of(candidate.Id, candidate.Label())),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = StreamStatsWidgetType.StyleKey,
							Values = StreamStatsStyles.WithMetric,
							SiblingValue = () => style.Value,
						},
					},
					new UiMultiSelectInput
					{
						Key = StreamStatsWidgetType.TilesKey,
						Label = Strings.Tiles(),
						Description = Strings.TilesDescription(),
						Binding = Bind.To(tiles),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. platform.Stats.Metrics.Select(candidate => UiOption.Of(candidate.Id, candidate.Label())),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = StreamStatsWidgetType.StyleKey,
							Values = StreamStatsStyles.WithTiles,
							SiblingValue = () => style.Value,
						},
					},
					new UiMultiSelectInput
					{
						Key = StreamStatsWidgetType.DetailsKey,
						Label = Strings.Details(),
						Description = Strings.DetailsDescription(),
						Binding = Bind.To(details),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. platform.Stats.Details.Select(candidate => UiOption.Of(candidate.Id, candidate.Label())),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = StreamStatsWidgetType.StyleKey,
							Values = StreamStatsStyles.WithDetails,
							SiblingValue = () => style.Value,
						},
					},
					new UiBooleanInput
					{
						Key = StreamStatsWidgetType.ThumbnailKey,
						Label = Strings.ShowThumbnail(),
						Binding = Bind.To(showThumbnail),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = StreamStatsWidgetType.StyleKey,
							Values = StreamStatsStyles.WithDetails,
							SiblingValue = () => style.Value,
						},
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border | UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.TransparentBackground |
						UiWidgetAppearanceFields.ColorVariables),
				],
			},
		};
	}
}
