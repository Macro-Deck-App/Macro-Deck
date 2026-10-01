using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsWidgetSample
{
	public static async Task<TwitchStatsViewState> BuildAsync(IWidgetSampleTextResolver text)
	{
		ArgumentNullException.ThrowIfNull(text);

		return new TwitchStatsViewState
		{
			HasAccount = true,
			IsLive = true,
			Viewers = TwitchStatsResolver.Count("1234"),
			Chatters = TwitchStatsResolver.Count("256"),
			Followers = TwitchStatsResolver.Count("12400"),
			Subscribers = TwitchStatsResolver.Count("842"),
			Title = await text.ResolveAsync(AppStrings.Integrations.Twitch.StatsWidget.SampleTitle())
				.ConfigureAwait(false),
			Category = await text.ResolveAsync(AppStrings.Integrations.Twitch.StatsWidget.SampleCategory())
				.ConfigureAwait(false),
			Uptime = "02:14:26",
			Points = [0.1, 0.2, 0.15, 0.3, 0.28, 0.45, 0.4, 0.6, 0.55, 0.8, 0.75, 1],
		};
	}
}
