using System.Threading.Channels;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.StreamChat;

public interface IStreamChatSink
{
	void SetAccounts(IReadOnlyList<ChatAccount> accounts);

	void Post(ChatEvent chatEvent);
}

public interface IStreamChatFeed
{
	event EventHandler<ChatChangedEventArgs>? Changed;

	IReadOnlyList<ChatAccount> Accounts { get; }

	ChatSnapshot Snapshot(string? accountId);
}

public sealed class StreamChatHub : IStreamChatSink, IStreamChatFeed, IDisposable
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
	private IReadOnlyList<ChatAccount> _accounts = [];
	private bool _accountsChanged;
	private volatile Dictionary<string, ChatSnapshot> _snapshots = new(StringComparer.Ordinal);

	public StreamChatHub(TimeProvider timeProvider, ILogger logger, ITwitchChatImages? images = null)
	{
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StreamChatHub>();
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

	private EventHandler<ChatChangedEventArgs>? _changed;
	private volatile bool _listenerJoined;

	public event EventHandler<ChatChangedEventArgs>? Changed
	{
		add
		{
			lock (_accountSync)
			{
				_changed += value;
				_listenerJoined = true;
			}
		}
		remove
		{
			lock (_accountSync)
			{
				_changed -= value;
			}
		}
	}

	public IReadOnlyList<ChatAccount> Accounts
	{
		get
		{
			lock (_accountSync)
			{
				return _accounts;
			}
		}
	}

	public void SetAccounts(IReadOnlyList<ChatAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		lock (_accountSync)
		{
			_accounts = [.. accounts];
			_accountsChanged = true;
		}
	}

	// Never waits: the socket read loop calls this, and a full message queue drops its oldest line instead.
	public void Post(ChatEvent chatEvent)
	{
		ArgumentNullException.ThrowIfNull(chatEvent);

		var item = new Sequenced(Interlocked.Increment(ref _sequence), chatEvent);
		var queue = chatEvent is ChatMessageReceived ? _messages : _control;
		queue.Writer.TryWrite(item);
	}

	public ChatSnapshot Snapshot(string? accountId)
	{
		ChatAccount? account;

		lock (_accountSync)
		{
			account = string.IsNullOrEmpty(accountId)
				? _accounts.Count > 0 ? _accounts[0] : null
				: _accounts.FirstOrDefault(candidate
					=> string.Equals(candidate.AccountId, accountId, StringComparison.Ordinal));
		}

		if (account is null)
		{
			return ChatSnapshot.None;
		}

		return _snapshots.TryGetValue(account.AccountId, out var snapshot)
			? snapshot with { Account = account }
			: new ChatSnapshot(account, false, [], 0);
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
					_logger.Error(exception, "Chat update failed");
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
			var added = new List<ChatMessage>();

			foreach (var item in batch)
			{
				Apply(item.Event, changed, added);
			}

			if (accountsChanged || changed.Count > 0)
			{
				PublishSnapshots(changed);
			}

			var listenerJoined = _listenerJoined;
			_listenerJoined = false;

			if (_images is not null && (accountsChanged || listenerJoined || added.Count > 0 || changed.Count > 0))
			{
				var retained = _histories.Values.SelectMany(history => history.Messages).ToList();
				_images.Pin([.. retained.SelectMany(message => message.Images())]);

				if (_changed is not null)
				{
					var retainedIds = retained.Select(message => message.MessageId).ToHashSet(StringComparer.Ordinal);
					var wanted = listenerJoined || accountsChanged
						? retained
						: added.Where(message => retainedIds.Contains(message.MessageId));
					foreach (var image in wanted.SelectMany(message => message.Images()))
					{
						_images.Request(image);
					}
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

	private void OnImageResolved(object? sender, TwitchChatImage image) => Post(new ChatImageResolved(image));

	private bool ReconcileAccounts()
	{
		IReadOnlyList<ChatAccount> accounts;

		lock (_accountSync)
		{
			if (!_accountsChanged)
			{
				return false;
			}

			_accountsChanged = false;
			accounts = _accounts;
		}

		var ids = accounts.Select(account => account.AccountId).ToHashSet(StringComparer.Ordinal);

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

	private void Apply(ChatEvent chatEvent, HashSet<string> changed, List<ChatMessage> added)
	{
		switch (chatEvent)
		{
			case ChatMessageReceived received when Find(received.AccountId) is { } history:
				if (history.Add(received.Message))
				{
					changed.Add(received.AccountId);
					added.Add(received.Message);
				}

				break;

			case ChatMessageDeleted deleted when Find(deleted.AccountId) is { } history:
				if (history.Remove(message
					=> string.Equals(message.MessageId, deleted.MessageId, StringComparison.Ordinal)))
				{
					changed.Add(deleted.AccountId);
				}

				break;

			case ChatUserCleared cleared when Find(cleared.AccountId) is { } history:
				if (history.Remove(message
					=> string.Equals(message.AuthorId, cleared.UserId, StringComparison.Ordinal)))
				{
					changed.Add(cleared.AccountId);
				}

				break;

			case ChatCleared cleared when Find(cleared.AccountId) is { } history:
				if (history.Remove(_ => true))
				{
					changed.Add(cleared.AccountId);
				}

				break;

			case ChatConnectionChanged connection when Find(connection.AccountId) is { } history:
				if (history.IsConnected != connection.IsConnected)
				{
					history.IsConnected = connection.IsConnected;
					changed.Add(connection.AccountId);
				}

				break;

			case ChatImageResolved resolved:
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
		var next = new Dictionary<string, ChatSnapshot>(StringComparer.Ordinal);

		foreach (var (accountId, history) in _histories)
		{
			if (!changed.Contains(accountId) && previous.TryGetValue(accountId, out var unchanged))
			{
				next[accountId] = unchanged;
				continue;
			}

			history.Version++;
			next[accountId] = new ChatSnapshot(null, history.IsConnected, [.. history.Messages], history.Version);
		}

		_snapshots = next;
	}

	private void Raise(string? accountId)
	{
		var handlers = _changed;

		if (handlers is null)
		{
			return;
		}

		foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<ChatChangedEventArgs>>())
		{
			try
			{
				handler(this, new ChatChangedEventArgs(accountId));
			}
#pragma warning disable CA1031 // A faulty session must not keep the others from updating.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Error(exception, "A chat subscriber failed");
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
		_logger.Warning("Chat arrived faster than it could be shown; {Count} message(s) were skipped",
			Interlocked.Exchange(ref _dropped, 0));
	}

	private readonly record struct Sequenced(long Sequence, ChatEvent Event);

	private sealed class History
	{
		private readonly LinkedList<ChatMessage> _messages = new();
		private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

		public bool IsConnected { get; set; }

		public long Version { get; set; }

		public IEnumerable<ChatMessage> Messages => _messages;

		public bool Add(ChatMessage message)
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

		public bool Remove(Func<ChatMessage, bool> predicate)
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
