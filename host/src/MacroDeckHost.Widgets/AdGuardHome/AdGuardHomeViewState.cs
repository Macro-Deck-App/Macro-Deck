using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.AdGuardHome;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.AdGuardHome;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal enum AdGuardHomeProtection
{
	Unknown,

	Enabled,

	Paused,

	Disabled
}

internal sealed record AdGuardHomeViewState
{
	public static readonly AdGuardHomeViewState NotConfigured = new()
	{
		Available = false,
		Message = Strings.NotConfigured(),
	};

	public string Title { get; init; } = "AdGuard Home";

	public bool Available { get; init; }

	public bool Connecting { get; init; }

	public AdGuardHomeProtection Protection { get; init; }

	public string? Version { get; init; }

	public LocalizedText Message { get; init; }

	public LocalizedText Failure { get; init; }

	public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

	public bool HasFailure => !Failure.IsEmpty;

	public string Value(string statistic) => Values.TryGetValue(statistic, out var value) ? value : "-";

	public bool Equals(AdGuardHomeViewState? other)
		=> other is not null &&
			Title == other.Title &&
			Available == other.Available &&
			Connecting == other.Connecting &&
			Protection == other.Protection &&
			Version == other.Version &&
			Message.Equals(other.Message) &&
			Failure.Equals(other.Failure) &&
			Values.Count == other.Values.Count &&
			Values.All(pair => other.Values.TryGetValue(pair.Key, out var value) && value == pair.Value);

	public override int GetHashCode() => HashCode.Combine(Title, Available, Protection, Version);
}

internal static class AdGuardHomeViewStateResolver
{
	public static AdGuardHomeViewState Resolve(
		AdGuardHomeWidgetOptions options,
		IAdGuardHomeInstances instances,
		DateTimeOffset now,
		LocalizedText failure = default)
	{
		var all = instances.Instances;
		if (all.Count == 0)
		{
			return AdGuardHomeViewState.NotConfigured with { Title = options.DisplayName ?? "AdGuard Home" };
		}

		var snapshot = options.InstanceId is null ? all[0] : instances.Find(options.InstanceId);
		if (snapshot is null)
		{
			return new AdGuardHomeViewState
			{
				Title = options.DisplayName ?? "AdGuard Home",
				Message = Strings.InstanceNotFound(),
			};
		}

		return Resolve(options, snapshot, now, failure);
	}

	public static AdGuardHomeViewState Resolve(
		AdGuardHomeWidgetOptions options,
		AdGuardHomeSnapshot snapshot,
		DateTimeOffset now,
		LocalizedText failure = default)
	{
		var title = options.DisplayName ?? snapshot.Title;

		if (!snapshot.IsConnected)
		{
			return new AdGuardHomeViewState
			{
				Title = title,
				Connecting = snapshot.Connection == AdGuardHomeConnection.Connecting,
				Message = snapshot.Connection switch
				{
					AdGuardHomeConnection.Connecting => Strings.Connecting(),
					AdGuardHomeConnection.Unauthorized => Strings.Unauthorized(),
					AdGuardHomeConnection.Timeout => Strings.Timeout(),
					AdGuardHomeConnection.Incompatible => Strings.Incompatible(),
					AdGuardHomeConnection.Redirected => Strings.Redirected(),
					_ => Strings.Offline(),
				},
			};
		}

		var protection = snapshot.ProtectionEnabled switch
		{
			true => AdGuardHomeProtection.Enabled,
			false when snapshot.DisabledUntil is { } until && until > now => AdGuardHomeProtection.Paused,
			false => AdGuardHomeProtection.Disabled,
			_ => AdGuardHomeProtection.Unknown,
		};

		return new AdGuardHomeViewState
		{
			Title = title,
			Available = true,
			Protection = protection,
			Version = snapshot.Version,
			Message = StatusLine(snapshot, protection, now),
			Failure = failure,
			Values = Values(snapshot.Statistics),
		};
	}

	internal static LocalizedText StatusLine(AdGuardHomeSnapshot snapshot, AdGuardHomeProtection protection,
		DateTimeOffset now)
	{
		if (snapshot.DnsRunning is false)
		{
			return Strings.DnsStopped();
		}

		return protection switch
		{
			AdGuardHomeProtection.Enabled => Strings.ProtectionEnabled(),
			AdGuardHomeProtection.Paused when snapshot.DisabledUntil is { } until => Remaining(until - now),
			_ => Strings.ProtectionDisabled(),
		};
	}

	internal static LocalizedText Remaining(TimeSpan remaining)
	{
		var minutes = (int)Math.Ceiling(Math.Max(remaining.TotalMinutes, 0));
		return minutes <= 60
			? Strings.ResumesInMinutes(count: Math.Max(minutes, 1))
			: Strings.ResumesInHours(count: (int)Math.Ceiling(remaining.TotalHours));
	}

	internal static IReadOnlyDictionary<string, string> Values(AdGuardHomeStatistics? stats)
	{
		if (stats is null)
		{
			return new Dictionary<string, string>();
		}

		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[AdGuardHomeWidgetType.DnsQueriesStatistic] = Count(stats.DnsQueries),
			[AdGuardHomeWidgetType.BlockedStatistic] = Count(stats.BlockedFiltering),
			[AdGuardHomeWidgetType.BlockedPercentageStatistic] =
				stats.BlockedPercentage.ToString("0.#", CultureInfo.CurrentCulture) + " %",
			[AdGuardHomeWidgetType.AverageProcessingTimeStatistic] =
				stats.AverageProcessingMilliseconds.ToString("0.#", CultureInfo.CurrentCulture) + " ms",
			[AdGuardHomeWidgetType.SafeBrowsingStatistic] = Count(stats.SafeBrowsing),
			[AdGuardHomeWidgetType.ParentalStatistic] = Count(stats.Parental),
			[AdGuardHomeWidgetType.SafeSearchStatistic] = Count(stats.SafeSearch),
		};
	}

	internal static string Count(long value)
		=> value switch
		{
			>= 1_000_000 => (value / 1_000_000d).ToString(value >= 100_000_000 ? "0" : "0.#", CultureInfo.CurrentCulture) +
				"M",
			>= 100_000 => Math.Floor(value / 1_000d).ToString("0", CultureInfo.CurrentCulture) + "K",
			>= 10_000 => (Math.Floor(value / 100d) / 10).ToString("0.#", CultureInfo.CurrentCulture) + "K",
			_ => value.ToString("N0", CultureInfo.CurrentCulture),
		};
}
