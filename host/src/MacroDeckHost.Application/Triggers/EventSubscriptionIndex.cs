using MacroDeckHost.Application.Caching;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Triggers;

public interface IEventSubscriptionIndex
{
	bool HasSubscribers(string qualifiedEventId);

	IReadOnlyList<EventSubscription> Find(string qualifiedEventId);

	EventSubscription? Find(EventTarget target);

	IReadOnlyList<EventSubscription> FindByProvider(string providerId);

	event Action? Changed;

	void Rebuild();

	void ReindexWidget(Guid widgetId, string? widgetData);

	void ReindexAutomation(Guid automationId, string? flows, bool enabled);

	void Remove(EventTriggerOwner owner);
}

public sealed class EventSubscriptionIndex : IEventSubscriptionIndex
{
	private readonly IFolderCache _folderCache;
	private readonly IAutomationCache _automationCache;
	private readonly Lock _writeLock = new();

	private volatile Snapshot _snapshot = Snapshot.Empty;

	public EventSubscriptionIndex(IFolderCache folderCache, IAutomationCache automationCache)
	{
		_folderCache = folderCache;
		_automationCache = automationCache;
	}

	public event Action? Changed;

	public bool HasSubscribers(string qualifiedEventId) => _snapshot.ByEventId.ContainsKey(qualifiedEventId);

	public IReadOnlyList<EventSubscription> Find(string qualifiedEventId)
		=> _snapshot.ByEventId.TryGetValue(qualifiedEventId, out var subscriptions) ? subscriptions : [];

	public EventSubscription? Find(EventTarget target)
		=> _snapshot.ByOwner.TryGetValue(target.Owner, out var subscriptions)
			? subscriptions.FirstOrDefault(s => s.TriggerId == target.TriggerId)
			: null;

	public IReadOnlyList<EventSubscription> FindByProvider(string providerId)
	{
		var matches = new List<EventSubscription>();
		foreach (var subscriptions in _snapshot.ByEventId.Values)
		{
			foreach (var subscription in subscriptions)
			{
				if (QualifiedId.TryParse(subscription.EventId, out var id) && id.OwnerId == providerId)
				{
					matches.Add(subscription);
				}
			}
		}

		return matches;
	}

	public void Rebuild()
	{
		lock (_writeLock)
		{
			var byOwner = new Dictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>>();
			foreach (var widget in _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets))
			{
				var owner = EventTriggerOwner.ForWidget(widget.Id);
				var subscriptions = EventTriggerParser.Parse(owner, widget.Data);
				if (subscriptions.Count > 0)
				{
					byOwner[owner] = subscriptions;
				}
			}

			foreach (var automation in _automationCache.GetAll().Where(automation => automation.Enabled))
			{
				var owner = EventTriggerOwner.ForAutomation(automation.Id);
				if (EventTriggerParser.ParseAutomation(owner, automation.Flows) is { } subscription)
				{
					byOwner[owner] = [subscription];
				}
			}

			Swap(byOwner);
		}
	}

	public void ReindexWidget(Guid widgetId, string? widgetData)
	{
		var owner = EventTriggerOwner.ForWidget(widgetId);
		Reindex(owner, EventTriggerParser.Parse(owner, widgetData));
	}

	public void ReindexAutomation(Guid automationId, string? flows, bool enabled)
	{
		var owner = EventTriggerOwner.ForAutomation(automationId);
		var subscription = enabled ? EventTriggerParser.ParseAutomation(owner, flows) : null;
		Reindex(owner, subscription is null ? [] : [subscription]);
	}

	public void Remove(EventTriggerOwner owner)
	{
		lock (_writeLock)
		{
			if (!_snapshot.ByOwner.ContainsKey(owner))
			{
				return;
			}

			var byOwner = new Dictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>>(_snapshot.ByOwner);
			byOwner.Remove(owner);
			Swap(byOwner);
		}
	}

	private void Reindex(EventTriggerOwner owner, IReadOnlyList<EventSubscription> subscriptions)
	{
		lock (_writeLock)
		{
			var current = _snapshot.ByOwner;
			if (subscriptions.Count == 0 && !current.ContainsKey(owner))
			{
				return;
			}

			var byOwner = new Dictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>>(current);
			if (subscriptions.Count == 0)
			{
				byOwner.Remove(owner);
			}
			else
			{
				byOwner[owner] = subscriptions;
			}

			Swap(byOwner);
		}
	}

	private void Swap(Dictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>> byOwner)
	{
		var byEventId = new Dictionary<string, IReadOnlyList<EventSubscription>>(StringComparer.Ordinal);
		foreach (var subscription in byOwner.Values.SelectMany(list => list))
		{
			if (byEventId.TryGetValue(subscription.EventId, out var existing))
			{
				((List<EventSubscription>)existing).Add(subscription);
			}
			else
			{
				byEventId[subscription.EventId] = new List<EventSubscription> { subscription };
			}
		}

		_snapshot = new Snapshot(byOwner, byEventId);
		Changed?.Invoke();
	}

	private sealed record Snapshot(
		IReadOnlyDictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>> ByOwner,
		IReadOnlyDictionary<string, IReadOnlyList<EventSubscription>> ByEventId)
	{
		public static readonly Snapshot Empty = new(
			new Dictionary<EventTriggerOwner, IReadOnlyList<EventSubscription>>(),
			new Dictionary<string, IReadOnlyList<EventSubscription>>(StringComparer.Ordinal));
	}
}
