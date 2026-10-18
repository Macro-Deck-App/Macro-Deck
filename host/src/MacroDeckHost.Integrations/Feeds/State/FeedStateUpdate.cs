using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Integrations.Feeds.State;

internal sealed record FeedRefreshOutcome(FeedStateDocument Document)
{
	public IReadOnlyList<FeedItem> NewItems { get; init; } = [];

	public bool ItemsChanged { get; init; }

	public bool FailureChanged { get; init; }
}

internal static class FeedStateUpdate
{
	public const int MaxCachedItems = 50;

	public const int MaxSeenKeys = 1000;

	public const int MaxNewItemEvents = 20;

	public static FeedStateDocument Fresh(FeedSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new FeedStateDocument { Url = settings.Url.AbsoluteUri, FilterSignature = settings.Filter.Signature };
	}

	// A different address is a different feed and starts over with a new baseline. Changed filters keep what
	// was seen, but forget the validators so the next request returns the whole feed to filter again.
	public static FeedStateDocument Adopt(FeedStateDocument? stored, FeedSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		if (stored is null || !string.Equals(stored.Url, settings.Url.AbsoluteUri, StringComparison.Ordinal))
		{
			return Fresh(settings);
		}

		return stored.FilterSignature == settings.Filter.Signature
			? stored
			: stored with
			{
				FilterSignature = settings.Filter.Signature,
				ETag = null,
				LastModified = null,
				LastUpdated = null
			};
	}

	public static FeedRefreshOutcome ApplyContent(
		FeedStateDocument current,
		ParsedFeed parsed,
		FeedSettings settings,
		FeedFetchResult fetch,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(current);
		ArgumentNullException.ThrowIfNull(parsed);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(fetch);

		var parsedItems = parsed.Items
			.Select(item => (Key: FeedItemKeys.Of(item), Item: item))
			.DistinctBy(entry => entry.Key, StringComparer.Ordinal)
			.ToList();
		var seen = current.Seen.ToHashSet(StringComparer.Ordinal);
		var fresh = parsedItems
			.Where(entry => !seen.Contains(entry.Key))
			.Select(entry => entry.Key)
			.ToHashSet(StringComparer.Ordinal);

		var items = parsedItems
			.Where(entry => settings.Filter.Matches(entry.Item))
			.Select(entry => FeedItem.From(entry.Key, entry.Item))
			.OrderByDescending(item => item.Published ?? DateTimeOffset.MinValue)
			.Take(MaxCachedItems)
			.ToList();

		List<FeedItem> newItems = current.Baselined ? [.. items.Where(item => fresh.Contains(item.Key))] : [];
		var cachedKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
		var unread = current.Unread
			.Concat(newItems.Select(item => item.Key))
			.Where(cachedKeys.Contains)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var currentKeys = parsedItems.Select(entry => entry.Key).ToList();
		var currentSet = currentKeys.ToHashSet(StringComparer.Ordinal);
		var nextSeen = currentKeys
			.Concat(current.Seen.Where(key => !currentSet.Contains(key)))
			.Take(Math.Max(MaxSeenKeys, currentKeys.Count))
			.ToList();

		var document = current with
		{
			ETag = fetch.ETag,
			LastModified = fetch.LastModified,
			FeedTitle = parsed.Title ?? current.FeedTitle,
			Baselined = true,
			LastUpdated = now,
			LastAttempt = now,
			Status = FeedStatus.Ok,
			Error = null,
			Failures = 0,
			Items = items,
			Seen = nextSeen,
			Unread = unread
		};

		return new FeedRefreshOutcome(document)
		{
			NewItems =
			[
				.. newItems.OrderBy(item => item.Published ?? DateTimeOffset.MinValue).TakeLast(MaxNewItemEvents)
			],
			ItemsChanged = current.Baselined && !SameItems(current.Items, items)
		};
	}

	public static FeedRefreshOutcome ApplyNotModified(
		FeedStateDocument current,
		FeedFetchResult fetch,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(current);
		ArgumentNullException.ThrowIfNull(fetch);

		return new FeedRefreshOutcome(current with
		{
			ETag = fetch.ETag ?? current.ETag,
			LastModified = fetch.LastModified ?? current.LastModified,
			LastUpdated = now,
			LastAttempt = now,
			Status = FeedStatus.NotModified,
			Error = null,
			Failures = 0
		});
	}

	public static FeedRefreshOutcome ApplyFailure(FeedStateDocument current, string error, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(current);

		return new FeedRefreshOutcome(current with
		{
			LastAttempt = now,
			Status = FeedStatus.Error,
			Error = error,
			Failures = current.Failures + 1
		})
		{
			FailureChanged = current.Status != FeedStatus.Error || current.Error != error
		};
	}

	public static FeedStateDocument MarkRead(FeedStateDocument current, string key)
	{
		ArgumentNullException.ThrowIfNull(current);

		return current.Unread.Contains(key, StringComparer.Ordinal)
			? current with { Unread = [.. current.Unread.Where(candidate => candidate != key)] }
			: current;
	}

	private static bool SameItems(IReadOnlyList<FeedItem> before, List<FeedItem> after)
	{
		return before.Count == after.Count && before.Zip(after).All(pair => pair.First.SameContent(pair.Second));
	}
}
