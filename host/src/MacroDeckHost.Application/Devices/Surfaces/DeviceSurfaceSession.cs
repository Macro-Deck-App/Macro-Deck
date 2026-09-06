using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// One device's live view of the deck: which profile it was assigned, where it currently is, how it got
/// there, and what was last pushed to it. Every device keeps its own, so navigating one device never
/// moves another.
/// </summary>
public sealed class DeviceSurfaceSession : IDisposable
{
	/// <summary>Matches the Angular client's history cap; the oldest entry is dropped past it.</summary>
	public const int HistoryLimit = 50;

	private readonly List<NavigationHistoryEntry> _history = [];

	private int _rebuildArmed;

	public DeviceSurfaceSession(
		Guid deviceId,
		string providerId,
		string providerDeviceId,
		IDeviceSurfaceProvider provider)
	{
		DeviceId = deviceId;
		ProviderId = providerId;
		ProviderDeviceId = providerDeviceId;
		Provider = provider;
	}

	public Guid DeviceId { get; }

	public string ProviderId { get; }

	public string ProviderDeviceId { get; }

	public IDeviceSurfaceProvider Provider { get; }

	/// <summary>Serializes rebuilds, pushes and navigation for this session against each other.</summary>
	public SemaphoreSlim Gate { get; } = new(1, 1);

	public DeviceSurfacePressTracker? Presses { get; set; }

	public ITimer? RebuildTimer { get; set; }

	/// <summary>The profile the device is currently rendering, which a cross-profile navigation moves.</summary>
	public string? ProfileId { get; set; }

	/// <summary>
	/// The device's assigned profile as of the last projection. Reassigning the device has to win over a
	/// cross-profile navigation, and the two are only distinguishable by remembering the assignment.
	/// </summary>
	public string? AssignedProfileId { get; set; }

	public bool HasProjected { get; set; }

	public string? FolderId { get; set; }

	public bool Online { get; set; } = true;

	public long Revision { get; private set; }

	public DeviceSurface? LastPushed { get; private set; }

	/// <summary>
	/// The owning folder of every widget on <see cref="LastPushed" />, keyed by widget id. Kept in step
	/// with the pushed surface because a press names a widget the device is rendering, and running its
	/// trigger needs the folder that widget lives in - which for a pinned widget is not the one on screen.
	/// </summary>
	public IReadOnlyDictionary<string, string> PushedWidgetFolderIds { get; private set; }
		= new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>True once the session has been discarded; nothing is projected or pushed afterwards.</summary>
	public bool Closed { get; set; }

	public IReadOnlyList<DeviceSurfaceSubscription> Subscriptions { get; set; } = [];

	public IReadOnlyList<NavigationHistoryEntry> History => _history;

	/// <summary>
	/// True for the caller that arms the debounce window; every further invalidation inside it coalesces
	/// into the same rebuild, which is what makes a bulk edit one push instead of one per widget.
	/// </summary>
	public bool TryArmRebuild() => Interlocked.CompareExchange(ref _rebuildArmed, 1, 0) == 0;

	public void DisarmRebuild() => Interlocked.Exchange(ref _rebuildArmed, 0);

	/// <summary>Stamps the next revision onto <paramref name="surface" /> and records it as pushed.</summary>
	public DeviceSurface Advance(DeviceSurface surface, IReadOnlyDictionary<string, string> widgetFolderIds)
	{
		var pushed = surface with { Revision = ++Revision };
		LastPushed = pushed;
		PushedWidgetFolderIds = widgetFolderIds;
		return pushed;
	}

	/// <summary>The folder the widget lives in, which is the folder its trigger runs against.</summary>
	public string? OwningFolderIdOf(string widgetId)
		=> PushedWidgetFolderIds.TryGetValue(widgetId, out var folderId) ? folderId : FolderId;

	public void PushHistory()
	{
		if (FolderId is not { } folderId)
		{
			return;
		}

		_history.Add(new NavigationHistoryEntry(folderId, ProfileId));
		if (_history.Count > HistoryLimit)
		{
			_history.RemoveAt(0);
		}
	}

	public NavigationHistoryEntry? PopHistory()
	{
		if (_history.Count == 0)
		{
			return null;
		}

		var entry = _history[^1];
		_history.RemoveAt(_history.Count - 1);
		return entry;
	}

	/// <summary>Drops history entries naming a folder of <paramref name="profileId" /> that no longer
	/// exists, so "back" cannot land on one. Entries from other profiles are left alone - their folders
	/// are not in <paramref name="knownFolderIds" /> to begin with.</summary>
	public void PruneHistory(string profileId, IReadOnlyCollection<string> knownFolderIds)
		=> _history.RemoveAll(entry =>
			string.Equals(entry.ProfileId, profileId, StringComparison.Ordinal) &&
			!knownFolderIds.Contains(entry.FolderId));

	public void ClearHistory() => _history.Clear();

	// The gate is deliberately not disposed: a rebuild that was already scheduled when the session was
	// discarded still takes it, sees Closed and gives up, and a disposed semaphore would turn that
	// orderly exit into an ObjectDisposedException on the caller's thread instead.
	public void Dispose()
	{
		RebuildTimer?.Dispose();
		Presses?.Dispose();
	}
}

public sealed record NavigationHistoryEntry(string FolderId, string? ProfileId);
