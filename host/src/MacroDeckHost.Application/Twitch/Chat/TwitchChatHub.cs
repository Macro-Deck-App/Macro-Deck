using System.Threading.Channels;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Twitch.Chat;

public interface ITwitchChatSink
{
	void SetAccounts(IReadOnlyList<TwitchChatAccount> accounts);

	void Post(TwitchChatEvent chatEvent);
}

public interface ITwitchChatFeed
{
	event EventHandler<TwitchChatChangedEventArgs>? Changed;

	IReadOnlyList<TwitchChatAccount> Accounts { get; }

	TwitchChatSnapshot Snapshot(string? accountId);
}

public sealed class TwitchChatHub : ITwitchChatSink, ITwitchChatFeed, IDisposable
{
	public const int HistoryLimit = 100;

	internal const int MessageQueueCapacity = 4096;

	internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

	private static readonly TimeSpan _dropLogInterval = TimeSpan.FromSeconds(30);

	private readonly Channel<Sequenced> _messages;
	private readonly Channel<Sequenced> _control;
	private readonly ITwitchChatImages? _images;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _accountSync = new();
	private readonly Lock _tickSync = new();
	private readonly Dictionary<string, History> _histories = new(StringComparer.Ordinal);

	private long _sequence;
	private long _dropped;
	private DateTimeOffset _lastDropLog = DateTimeOffset.MinValue;
	private IReadOnlyList<TwitchChatAccount> _accounts = [];
	private bool _accountsChanged;
	private volatile Dictionary<string, TwitchChatSnapshot> _snapshots = new(StringComparer.Ordinal);

	public TwitchChatHub(TimeProvider timeProvider, ILogger logger, ITwitchChatImages? images = null)
	{
		_timeProvider = timeProvider;
		_logger = logger.ForContext<TwitchChatHub>();
		_images = images;

		_messages = Channel.CreateBounded<Sequenced>(
			new BoundedChannelOptions(MessageQueueCapacity)
			{
				FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false
			},
			_ => Interlocked.Increment(ref _dropped));
		_control = Channel.CreateUnbounded<Sequenced>(new UnboundedChannelOptions { SingleReader = true });

		if (_images is not null)
		{
			_images.Resolved += OnImageResolved;
		}
	}

	public event EventHandler<TwitchChatChangedEventArgs>? Changed;

	public IReadOnlyList<TwitchChatAccount> Accounts
	{
		get
		{
			lock (_accountSync)
			{
				return _accounts;
			}
		}
	}

	public void SetAccounts(IReadOnlyList<TwitchChatAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		lock (_accountSync)
		{
			_accounts = [.. accounts];
			_accountsChanged = true;
		}
	}

	// Never waits: the socket read loop calls this, and a full message queue drops its oldest line instead.
	public void Post(TwitchChatEvent chatEvent)
	{
		ArgumentNullException.ThrowIfNull(chatEvent);

		var item = new Sequenced(Interlocked.Increment(ref _sequence), chatEvent);
		var queue = chatEvent is TwitchChatMessageReceived ? _messages : _control;
		queue.Writer.TryWrite(item);
	}

	public TwitchChatSnapshot Snapshot(string? accountId)
	{
		TwitchChatAccount? account;

		lock (_accountSync)
		{
			account = string.IsNullOrEmpty(accountId)
				? _accounts.Count > 0 ? _accounts[0] : null
				: _accounts.FirstOrDefault(candidate
					=> string.Equals(candidate.UserId, accountId, StringComparison.Ordinal));
		}

		if (account is null)
		{
			return TwitchChatSnapshot.None;
		}

		return _snapshots.TryGetValue(account.UserId, out var snapshot)
			? snapshot with { Account = account }
			: new TwitchChatSnapshot(account, false, [], 0);
	}

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(TickInterval, _timeProvider);

		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				try
				{
					Tick();
				}
#pragma warning disable CA1031 // One bad tick must not stop every chat widget for the rest of the run.
				catch (Exception exception)
#pragma warning restore CA1031
				{
					_logger.Error(exception, "Twitch chat update failed");
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	internal void Tick()
	{
		lock (_tickSync)
		{
			var batch = new List<Sequenced>();

			while (_control.Reader.TryRead(out var control))
			{
				batch.Add(control);
			}

			while (_messages.Reader.TryRead(out var message))
			{
				batch.Add(message);
			}

			batch.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

			var accountsChanged = ReconcileAccounts();
			var changed = new HashSet<string>(StringComparer.Ordinal);
			var added = new List<TwitchChatMessage>();

			foreach (var item in batch)
			{
				Apply(item.Event, changed, added);
			}

			if (accountsChanged || changed.Count > 0)
			{
				PublishSnapshots(changed);
			}

			if (_images is not null && (accountsChanged || added.Count > 0 || changed.Count > 0))
			{
				_images.Pin([.. _histories.Values.SelectMany(history => history.Messages).SelectMany(m => m.Images())]);

				foreach (var image in added.SelectMany(message => message.Images()))
				{
					_images.Request(image);
				}
			}

			LogDropped();

			if (accountsChanged)
			{
				Raise(null);
			}

			foreach (var accountId in changed)
			{
				Raise(accountId);
			}
		}
	}

	public void Dispose()
	{
		if (_images is not null)
		{
			_images.Resolved -= OnImageResolved;
		}

		_messages.Writer.TryComplete();
		_control.Writer.TryComplete();
	}

	private void OnImageResolved(object? sender, TwitchChatImage image) => Post(new TwitchChatImageResolved(image));

	private bool ReconcileAccounts()
	{
		IReadOnlyList<TwitchChatAccount> accounts;

		lock (_accountSync)
		{
			if (!_accountsChanged)
			{
				return false;
			}

			_accountsChanged = false;
			accounts = _accounts;
		}

		var ids = accounts.Select(account => account.UserId).ToHashSet(StringComparer.Ordinal);

		foreach (var removed in _histories.Keys.Where(id => !ids.Contains(id)).ToList())
		{
			_histories.Remove(removed);
		}

		foreach (var id in ids)
		{
			_histories.TryAdd(id, new History());
		}

		return true;
	}

	private void Apply(TwitchChatEvent chatEvent, HashSet<string> changed, List<TwitchChatMessage> added)
	{
		switch (chatEvent)
		{
			case TwitchChatMessageReceived received when Find(received.AccountId) is { } history:
				if (history.Add(received.Message))
				{
					changed.Add(received.AccountId);
					added.Add(received.Message);
				}

				break;

			case TwitchChatMessageDeleted deleted when Find(deleted.AccountId) is { } history:
				if (history.Remove(message
					=> string.Equals(message.MessageId, deleted.MessageId, StringComparison.Ordinal)))
				{
					changed.Add(deleted.AccountId);
				}

				break;

			case TwitchChatUserCleared cleared when Find(cleared.AccountId) is { } history:
				if (history.Remove(message
					=> string.Equals(message.ChatterId, cleared.UserId, StringComparison.Ordinal)))
				{
					changed.Add(cleared.AccountId);
				}

				break;

			case TwitchChatCleared cleared when Find(cleared.AccountId) is { } history:
				if (history.Remove(_ => true))
				{
					changed.Add(cleared.AccountId);
				}

				break;

			case TwitchChatConnectionChanged connection when Find(connection.AccountId) is { } history:
				if (history.IsConnected != connection.IsConnected)
				{
					history.IsConnected = connection.IsConnected;
					changed.Add(connection.AccountId);
				}

				break;

			case TwitchChatImageResolved resolved:
				foreach (var (accountId, history) in _histories)
				{
					if (history.Messages.Any(message => message.Images().Contains(resolved.Image)))
					{
						changed.Add(accountId);
					}
				}

				break;

			default:
				break;
		}
	}

	private History? Find(string accountId) => _histories.GetValueOrDefault(accountId);

	private void PublishSnapshots(HashSet<string> changed)
	{
		var previous = _snapshots;
		var next = new Dictionary<string, TwitchChatSnapshot>(StringComparer.Ordinal);

		foreach (var (accountId, history) in _histories)
		{
			if (!changed.Contains(accountId) && previous.TryGetValue(accountId, out var unchanged))
			{
				next[accountId] = unchanged;
				continue;
			}

			history.Version++;
			next[accountId] = new TwitchChatSnapshot(null, history.IsConnected, [.. history.Messages], history.Version);
		}

		_snapshots = next;
	}

	private void Raise(string? accountId)
	{
		var handlers = Changed;

		if (handlers is null)
		{
			return;
		}

		foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<TwitchChatChangedEventArgs>>())
		{
			try
			{
				handler(this, new TwitchChatChangedEventArgs(accountId));
			}
#pragma warning disable CA1031 // A faulty session must not keep the others from updating.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Error(exception, "A Twitch chat subscriber failed");
			}
		}
	}

	private void LogDropped()
	{
		var now = _timeProvider.GetUtcNow();

		if (Interlocked.Read(ref _dropped) == 0 || now - _lastDropLog < _dropLogInterval)
		{
			return;
		}

		_lastDropLog = now;
		_logger.Warning("Twitch chat arrived faster than it could be shown; {Count} message(s) were skipped",
			Interlocked.Exchange(ref _dropped, 0));
	}

	private readonly record struct Sequenced(long Sequence, TwitchChatEvent Event);

	private sealed class History
	{
		private readonly LinkedList<TwitchChatMessage> _messages = new();
		private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

		public bool IsConnected { get; set; }

		public long Version { get; set; }

		public IEnumerable<TwitchChatMessage> Messages => _messages;

		public bool Add(TwitchChatMessage message)
		{
			if (!_ids.Add(message.MessageId))
			{
				return false;
			}

			_messages.AddLast(message);

			while (_messages.Count > HistoryLimit)
			{
				_ids.Remove(_messages.First!.Value.MessageId);
				_messages.RemoveFirst();
			}

			return true;
		}

		public bool Remove(Func<TwitchChatMessage, bool> predicate)
		{
			var removed = false;

			for (var node = _messages.First; node is not null;)
			{
				var next = node.Next;

				if (predicate(node.Value))
				{
					_ids.Remove(node.Value.MessageId);
					_messages.Remove(node);
					removed = true;
				}

				node = next;
			}

			return removed;
		}
	}
}
