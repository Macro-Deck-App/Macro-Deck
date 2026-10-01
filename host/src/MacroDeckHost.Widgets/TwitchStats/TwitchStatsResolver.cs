using System.Globalization;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsResolver
{
	public const string VariablePrefix = "twitch_";

	public static string VariableName(string variableKey, string name) => $"{VariablePrefix}{variableKey}_{name}";

	public static TwitchStatsViewState Resolve(
		VariableRegistry variables,
		TwitchStatsAccount? account,
		IReadOnlyList<double> history,
		ITwitchStreamThumbnails thumbnails,
		string metric = TwitchStatsMetrics.Viewers)
	{
		if (account is null)
		{
			return TwitchStatsViewState.Empty;
		}

		string? Read(string name)
		{
			var variable = variables.FindByName(VariableScope.Global, null, VariableName(account.VariableKey, name));

			return variable is not null && variables.IsAvailable(variable.Id) ? variable.Value : null;
		}

		var live = string.Equals(Read("is_live"), "true", StringComparison.OrdinalIgnoreCase);
		var thumbnailUrl = live && Read("stream_thumbnail_url") is { Length: > 0 } url ? url : null;

		thumbnails.Track(account.UserId, thumbnailUrl);

		return new TwitchStatsViewState
		{
			HasAccount = true,
			IsLive = live,
			ChannelName = Read("display_name") ?? account.Label,
			Viewers = live ? Count(Read("viewer_count")) : TwitchStatsViewState.Unavailable,
			Chatters = live ? Count(Read("chatter_count")) : TwitchStatsViewState.Unavailable,
			Followers = Count(Read("follower_count")),
			Subscribers = Count(Read("subscriber_count")),
			Title = live ? Read("stream_title") ?? string.Empty : string.Empty,
			Category = live ? Read("stream_category") ?? string.Empty : string.Empty,
			Uptime = live ? Uptime(Read("uptime_seconds")) : string.Empty,
			Points = live || !TwitchStatsMetrics.OnlyWhileLive(metric) ? Normalize(history) : [],
			Thumbnail = live ? thumbnails.Find(account.UserId) : null,
		};
	}

	internal static string Count(string? value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
			!double.IsFinite(number) ||
			number < 0)
		{
			return TwitchStatsViewState.Unavailable;
		}

		var whole = Math.Floor(number);

		return whole switch
		{
			>= 1_000_000 => (whole / 1_000_000).ToString(whole >= 100_000_000 ? "0" : "0.#", CultureInfo.CurrentCulture) + "M",
			>= 100_000 => Math.Floor(whole / 1_000).ToString("0", CultureInfo.CurrentCulture) + "K",
			>= 10_000 => (Math.Floor(whole / 100) / 10).ToString("0.#", CultureInfo.CurrentCulture) + "K",
			_ => whole.ToString("N0", CultureInfo.CurrentCulture),
		};
	}

	internal static string Uptime(string? seconds)
	{
		if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var total) ||
			!double.IsFinite(total) ||
			total < 0)
		{
			return string.Empty;
		}

		var span = TimeSpan.FromSeconds(Math.Floor(total));

		return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}");
	}

	internal static double[] Normalize(IReadOnlyList<double> samples)
	{
		if (samples.Count < 2)
		{
			return [];
		}

		var low = samples.Min();
		var high = samples.Max();
		var range = high - low;

		return [.. samples.Select(sample => range > 0 ? Math.Round((sample - low) / range, 4) : 0.5)];
	}
}
