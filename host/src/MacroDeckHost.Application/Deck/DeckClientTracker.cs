using MacroDeck.Sdk.Decks;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Deck;

public sealed record DeckClientChange(DeckClientChangedEventArgs? Moved);

public sealed class DeckClientTracker
{
	private readonly Dictionary<string, DeckClient> _clients = new(StringComparer.Ordinal);
	private readonly Lock _sync = new();
	private readonly ILogger _logger;

	public DeckClientTracker(ILogger logger)
	{
		_logger = logger.ForContext<DeckClientTracker>();
	}

	public event EventHandler<DeckClientChangedEventArgs>? ClientChanged;

	public event Action? StateChanged;

	public IReadOnlyList<DeckClient> Snapshot()
	{
		lock (_sync)
		{
			return [.. _clients.Values];
		}
	}

	public DeckClientChange? Report(string clientId, Guid? deviceId, string profileId, string folderId)
	{
		var device = deviceId?.ToString("D");
		lock (_sync)
		{
			var replacedOther = device is not null &&
				RemoveWhereLocked(client => client.DeviceId == device && client.ClientId != clientId);

			_clients.TryGetValue(clientId, out var before);
			var moved = before is null || before.ProfileId != profileId || before.FolderId != folderId;
			if (!moved && before!.DeviceId == device)
			{
				return replacedOther ? new DeckClientChange(null) : null;
			}

			var client = new DeckClient
			{
				ClientId = clientId,
				DeviceId = device,
				ProfileId = profileId,
				FolderId = folderId
			};
			_clients[clientId] = client;

			return new DeckClientChange(moved
				? new DeckClientChangedEventArgs(client, before?.ProfileId, before?.FolderId)
				: null);
		}
	}

	public DeckClientChange? Remove(string clientId)
	{
		lock (_sync)
		{
			return _clients.Remove(clientId) ? new DeckClientChange(null) : null;
		}
	}

	public DeckClientChange? RemoveDevice(Guid deviceId)
	{
		var device = deviceId.ToString("D");
		lock (_sync)
		{
			return RemoveWhereLocked(client => client.DeviceId == device) ? new DeckClientChange(null) : null;
		}
	}

	public void Publish(DeckClientChange? change)
	{
		if (change is null)
		{
			return;
		}

		if (change.Moved is { } moved && ClientChanged is { } clientChanged)
		{
			foreach (var handler in clientChanged.GetInvocationList().Cast<EventHandler<DeckClientChangedEventArgs>>())
			{
				Invoke(() => handler(this, moved));
			}
		}

		if (StateChanged is { } stateChanged)
		{
			foreach (var handler in stateChanged.GetInvocationList().Cast<Action>())
			{
				Invoke(handler);
			}
		}
	}

	private void Invoke(Action handler)
	{
		try
		{
			handler();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "A deck client subscriber failed");
		}
	}

	private bool RemoveWhereLocked(Func<DeckClient, bool> match)
	{
		var ids = _clients.Values.Where(match).Select(client => client.ClientId).ToList();
		foreach (var id in ids)
		{
			_clients.Remove(id);
		}

		return ids.Count > 0;
	}
}
