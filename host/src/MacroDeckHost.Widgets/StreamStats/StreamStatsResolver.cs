using System.Globalization;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsResolver
{
	public const string IsLiveVariable = "is_live";

	public const string DisplayNameVariable = "display_name";

	public const string ThumbnailUrlVariable = "stream_thumbnail_url";

	public static string VariableName(StreamStatsAccount account, string name)
	{
		ArgumentNullException.ThrowIfNull(account);

		return account.VariablePrefix + name;
	}

	public static StreamStatsViewState Resolve(
		StreamPlatform platform,
		VariableRegistry variables,
		StreamStatsAccount? account,
		IReadOnlyList<double> history,
		IStreamThumbnails thumbnails,
		string? metric = null)
	{
		ArgumentNullException.ThrowIfNull(platform);

		if (account is null)
		{
			return StreamStatsViewState.Empty;
		}

		string? Read(string name)
		{
			var variable = variables.FindByName(VariableScope.Global, null, VariableName(account, name));

			return variable is not null && variables.IsAvailable(variable.Id) ? variable.Value : null;
		}

		var stats = platform.Stats;
		var live = string.Equals(Read(IsLiveVariable), "true", StringComparison.OrdinalIgnoreCase);
		var thumbnailUrl = live && Read(ThumbnailUrlVariable) is { Length: > 0 } url ? url : null;
		var graphedOnlyWhileLive = stats.Metric(metric ?? stats.DefaultMetric)?.OnlyWhileLive ?? false;

		thumbnails.Track(account.AccountId, thumbnailUrl);

		return new StreamStatsViewState
		{
			HasAccount = true,
			IsLive = live,
			ChannelName = Read(DisplayNameVariable) ?? account.Label,
			Metrics = stats.Metrics.ToDictionary(entry => entry.Id,
				entry => live || !entry.OnlyWhileLive
					? Count(Read(entry.VariableName))
					: StreamStatsViewState.Unavailable,
				StringComparer.Ordinal),
			Details = stats.Details.ToDictionary(entry => entry.Id,
				entry => !live
					? string.Empty
					: entry.Kind == StreamStatsDetailKind.Duration
						? Uptime(Read(entry.VariableName))
						: Read(entry.VariableName) ?? string.Empty,
				StringComparer.Ordinal),
			Points = live || !graphedOnlyWhileLive ? Normalize(history) : [],
			Thumbnail = live ? thumbnails.Find(account.AccountId) : null,
		};
	}

	internal static string Count(string? value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
			!double.IsFinite(number) ||
			number < 0)
		{
			return StreamStatsViewState.Unavailable;
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
