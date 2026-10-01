using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Twitch.StatsWidget;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsWidgetConfigView
{
	public static UiElement Build(JsonElement data, IReadOnlyList<TwitchStatsAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		var account = new UiState<string>(WidgetConfigJson.ReadString(data, TwitchStatsWidgetType.AccountKey) ??
			string.Empty);
		var options = TwitchStatsWidgetSettings.Options(data);
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
					new UiHeading { Key = "stats-heading", Text = Strings.Name() },
					new UiChoiceInput
					{
						Key = TwitchStatsWidgetType.AccountKey,
						Label = AppStrings.Integrations.Twitch.Actions.AccountLabel(),
						Description = Strings.AccountDescription(),
						Placeholder = AppStrings.Integrations.Twitch.ChatWidget.FirstAccount(),
						Binding = Bind.To(account),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. accounts.Select(candidate => UiOption.Of(candidate.UserId, candidate.Label)),
						]),
					},
					new UiChoiceInput
					{
						Key = TwitchStatsWidgetType.StyleKey,
						Label = Strings.Style(),
						Description = Strings.StyleDescription(),
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(TwitchStatsStyles.Overview, Strings.StyleOverview()),
							UiOption.Of(TwitchStatsStyles.StatsRow, Strings.StyleStatsRow()),
							UiOption.Of(TwitchStatsStyles.LiveRow, Strings.StyleLiveRow()),
							UiOption.Of(TwitchStatsStyles.ValueGraph, Strings.StyleValueGraph()),
							UiOption.Of(TwitchStatsStyles.Value, Strings.StyleValue()),
						]),
					},
					new UiChoiceInput
					{
						Key = TwitchStatsWidgetType.MetricKey,
						Label = Strings.Metric(),
						Description = Strings.MetricDescription(),
						Binding = Bind.To(metric),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(TwitchStatsMetrics.Viewers, Strings.Viewers()),
							UiOption.Of(TwitchStatsMetrics.Chatters, Strings.Chatters()),
							UiOption.Of(TwitchStatsMetrics.Followers, Strings.Followers()),
							UiOption.Of(TwitchStatsMetrics.Subscribers, Strings.Subscribers()),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = TwitchStatsWidgetType.StyleKey,
							Values = TwitchStatsStyles.WithMetric,
							SiblingValue = () => style.Value,
						},
					},
					new UiMultiSelectInput
					{
						Key = TwitchStatsWidgetType.TilesKey,
						Label = Strings.Tiles(),
						Description = Strings.TilesDescription(),
						Binding = Bind.To(tiles),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. TwitchStatsMetrics.All.Select(id => UiOption.Of(id, TwitchStatsWidgetView.Label(id))),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = TwitchStatsWidgetType.StyleKey,
							Values = TwitchStatsStyles.WithTiles,
							SiblingValue = () => style.Value,
						},
					},
					new UiMultiSelectInput
					{
						Key = TwitchStatsWidgetType.DetailsKey,
						Label = Strings.Details(),
						Description = Strings.DetailsDescription(),
						Binding = Bind.To(details),
						Reorderable = true,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(TwitchStatsDetails.Title, AppStrings.Integrations.Twitch.Variables.StreamTitle()),
							UiOption.Of(TwitchStatsDetails.Category,
								AppStrings.Integrations.Twitch.Variables.StreamCategory()),
							UiOption.Of(TwitchStatsDetails.Uptime, Strings.Uptime()),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = TwitchStatsWidgetType.StyleKey,
							Values = TwitchStatsStyles.WithDetails,
							SiblingValue = () => style.Value,
						},
					},
					new UiBooleanInput
					{
						Key = TwitchStatsWidgetType.ThumbnailKey,
						Label = Strings.ShowThumbnail(),
						Binding = Bind.To(showThumbnail),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = TwitchStatsWidgetType.StyleKey,
							Values = TwitchStatsStyles.WithDetails,
							SiblingValue = () => style.Value,
						},
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border | UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.TransparentBackground),
				],
			},
		};
	}
}
