using MacroDeckHost.Integrations.Feeds.State;

namespace MacroDeckHost.Integrations.Feeds.Connections;

internal delegate void FeedItemOpener(Guid entryId, string itemKey, Guid? widgetId);

internal interface IFeedSource
{
	event EventHandler? Changed;

	IReadOnlyList<FeedSnapshot> Feeds { get; }

	FeedSnapshot? Find(Guid entryId);

	void Open(Guid entryId, string itemKey, Guid? widgetId);
}

internal sealed class FeedHub : IFeedSource
{
	private readonly Lock _sync = new();

	private FeedSnapshot[] _feeds = [];
	private FeedItemOpener? _opener;

	public event EventHandler? Changed;

	public IReadOnlyList<FeedSnapshot> Feeds
	{
		get
		{
			lock (_sync)
			{
				return _feeds;
			}
		}
	}

	public FeedSnapshot? Find(Guid entryId)
	{
		return Feeds.FirstOrDefault(feed => feed.EntryId == entryId);
	}

	public void Open(Guid entryId, string itemKey, Guid? widgetId)
	{
		FeedItemOpener? opener;
		lock (_sync)
		{
			opener = _opener;
		}

		opener?.Invoke(entryId, itemKey, widgetId);
	}

	public void UseOpener(FeedItemOpener? opener)
	{
		lock (_sync)
		{
			_opener = opener;
		}
	}

	public void Replace(IReadOnlyList<FeedSnapshot> feeds)
	{
		ArgumentNullException.ThrowIfNull(feeds);

		lock (_sync)
		{
			_feeds = [.. feeds];
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Update(FeedSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		lock (_sync)
		{
			var index = Array.FindIndex(_feeds, feed => feed.EntryId == snapshot.EntryId);
			if (index < 0)
			{
				return;
			}

			var next = (FeedSnapshot[])_feeds.Clone();
			next[index] = snapshot;
			_feeds = next;
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}
}
