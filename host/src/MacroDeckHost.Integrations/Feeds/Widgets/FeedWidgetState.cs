using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Feeds.State;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal enum FeedWidgetStatus
{
	Ok,
	NoFeeds,
	FeedMissing,
	GroupEmpty,
	NoArticles
}

internal sealed record FeedRow(string Id, Guid EntryId, string Key, string Title, string Meta)
{
	public string? Description { get; init; }

	public string? ImageUrl { get; init; }

	public UiResource? Image { get; init; }

	public bool Unread { get; init; }
}

internal sealed record FeedWidgetState(FeedWidgetStatus Status, LocalizedText Heading, IReadOnlyList<FeedRow> Rows)
{
	public static readonly FeedWidgetState Empty = new(FeedWidgetStatus.NoFeeds, default, []);

	public LocalizedText Message => Status switch
	{
		FeedWidgetStatus.NoFeeds => Strings.NoFeeds(),
		FeedWidgetStatus.FeedMissing => Strings.FeedMissing(),
		FeedWidgetStatus.GroupEmpty => Strings.GroupEmpty(),
		FeedWidgetStatus.NoArticles => Strings.NoArticles(),
		_ => default
	};

	public bool Equals(FeedWidgetState? other)
	{
		return other is not null
			&& Status == other.Status
			&& Heading.Equals(other.Heading)
			&& Rows.SequenceEqual(other.Rows);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Status, Rows.Count);
	}
}

internal sealed class FeedDateFormat
{
	private readonly CultureInfo _culture;
	private readonly TimeZoneInfo _zone;
	private readonly DateTimeOffset _now;

	public FeedDateFormat(CultureInfo culture, TimeZoneInfo zone, DateTimeOffset now)
	{
		_culture = culture;
		_zone = zone;
		_now = now;
	}

	public static CultureInfo CultureOf(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return CultureInfo.CurrentCulture;
		}

		try
		{
			return CultureInfo.GetCultureInfo(name);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.CurrentCulture;
		}
	}

	public string Format(DateTimeOffset? value)
	{
		if (value is not { } date)
		{
			return string.Empty;
		}

		var local = TimeZoneInfo.ConvertTime(date, _zone);
		var today = TimeZoneInfo.ConvertTime(_now, _zone).Date;
		return local.Date == today ? local.ToString("t", _culture) : local.ToString("d", _culture);
	}

	public string Long(DateTimeOffset? value)
	{
		return value is { } date ? TimeZoneInfo.ConvertTime(date, _zone).ToString("g", _culture) : string.Empty;
	}
}

internal static class FeedWidgetResolver
{
	private const string Separator = " · ";

	public static FeedWidgetState Resolve(
		FeedWidgetOptions options,
		IReadOnlyList<FeedSnapshot> feeds,
		int take,
		FeedDateFormat format,
		Func<string, UiResource?> image)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(feeds);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(image);

		if (feeds.Count == 0)
		{
			return FeedWidgetState.Empty;
		}

		IReadOnlyList<FeedSnapshot> selected;
		LocalizedText heading;
		switch (options.Source)
		{
			case FeedWidgetTypes.FeedSource when options.FeedId is { } feedId:
				if (feeds.FirstOrDefault(feed => feed.EntryId == feedId) is not { } match)
				{
					return new FeedWidgetState(FeedWidgetStatus.FeedMissing, default, []);
				}

				selected = [match];
				heading = match.Title;
				break;
			case FeedWidgetTypes.GroupSource when options.Group is { } group:
				selected =
				[
					.. feeds.Where(feed => string.Equals(feed.Group, group, StringComparison.OrdinalIgnoreCase))
				];
				if (selected.Count == 0)
				{
					return new FeedWidgetState(FeedWidgetStatus.GroupEmpty, group, []);
				}

				heading = group;
				break;
			default:
				selected = feeds;
				heading = Strings.AllFeeds();
				break;
		}

		var showSource = selected.Count > 1;
		var rows = selected
			.SelectMany(feed => feed.Items.Select(item => (Feed: feed, Item: item)))
			.OrderByDescending(entry => entry.Item.Published ?? DateTimeOffset.MinValue)
			.Take(Math.Max(1, take))
			.Select(entry => Row(entry.Feed, entry.Item, showSource, format, image))
			.ToList();

		return new FeedWidgetState(rows.Count == 0 ? FeedWidgetStatus.NoArticles : FeedWidgetStatus.Ok, heading, rows);
	}

	// Item keys can hold any character of a link, so the node key is a hash of it.
	public static string RowId(Guid entryId, string key)
	{
		return $"row-{entryId:N}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]}";
	}

	private static FeedRow Row(
		FeedSnapshot feed,
		FeedItem item,
		bool showSource,
		FeedDateFormat format,
		Func<string, UiResource?> image)
	{
		var date = format.Format(item.Published);
		var meta = showSource
			? string.Join(Separator, new[] { feed.Title, date }.Where(part => part.Length > 0))
			: date;

		return new FeedRow(RowId(feed.EntryId, item.Key), feed.EntryId, item.Key, item.Title, meta)
		{
			Description = item.Description,
			ImageUrl = item.ImageUrl,
			Image = item.ImageUrl is { } url ? image(url) : null,
			Unread = feed.IsUnread(item.Key)
		};
	}
}
