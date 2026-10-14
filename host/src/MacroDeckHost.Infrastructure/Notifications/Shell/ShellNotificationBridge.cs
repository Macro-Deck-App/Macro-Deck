using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MacroDeckHost.Infrastructure.Notifications.Shell;

public sealed class ShellNotificationBridge : IShellNotificationBridge, IDisposable
{
	public static readonly TimeSpan AttachmentWindow = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan Lease = TimeSpan.FromSeconds(5);

	private const int QueueCapacity = 32;

	private readonly TimeProvider _time;

	// A full queue rejects the newest notification rather than evicting an older one: the rejected
	// caller learns about it right away and falls back, where an evicted entry would only surface as
	// its dispatcher timing out on the lease.
	private readonly Channel<ShellNotification> _queue = Channel.CreateBounded<ShellNotification>(
		new BoundedChannelOptions(QueueCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true
		});

	private readonly ConcurrentDictionary<long, PendingNotification> _pending = new();

	private readonly ITimer _detach;
	private readonly Lock _attachment = new();

	private long _nextId;
	private long _lastPollTicks = long.MinValue;
	private int _waiting;
	private bool _reportedAttached;

	public ShellNotificationBridge(TimeProvider time)
	{
		_time = time;
		_detach = time.CreateTimer(_ => OnAttachmentWindowElapsed(),
			null,
			Timeout.InfiniteTimeSpan,
			Timeout.InfiniteTimeSpan);
	}

	public event Action? AttachmentChanged;

	public void Dispose()
	{
		_detach.Dispose();
	}

	public bool IsAttached
	{
		get
		{
			var lastPoll = Interlocked.Read(ref _lastPollTicks);
			return lastPoll != long.MinValue && _time.GetUtcNow().UtcTicks - lastPoll < AttachmentWindow.Ticks;
		}
	}

	public async Task<bool> TryDispatchAsync(
		string title,
		string message,
		CancellationToken cancellationToken = default)
	{
		if (!IsAttached)
		{
			return false;
		}

		var id = Interlocked.Increment(ref _nextId);
		// The lease starts before the notification becomes visible to the shell, so a notification the
		// shell has seen is always one whose clock is already running.
		var pending = new PendingNotification(
			new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
			new CancellationTokenSource(Lease, _time));
		_pending[id] = pending;

		try
		{
			if (!_queue.Writer.TryWrite(new ShellNotification(id, title, message)))
			{
				return false;
			}

			using var linked =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pending.Lease.Token);
			return await pending.Result.Task.WaitAsync(linked.Token);
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		finally
		{
			_pending.TryRemove(id, out _);
			pending.Lease.Dispose();
		}
	}

	public async Task<IReadOnlyList<ShellNotification>> WaitAsync(
		TimeSpan hold,
		CancellationToken cancellationToken = default)
	{
		if (Interlocked.CompareExchange(ref _waiting, 1, 0) != 0)
		{
			throw new InvalidOperationException("Another caller is already waiting for shell notifications.");
		}

		try
		{
			bool attached;
			lock (_attachment)
			{
				Interlocked.Exchange(ref _lastPollTicks, _time.GetUtcNow().UtcTicks);
				_detach.Change(AttachmentWindow, Timeout.InfiniteTimeSpan);
				attached = !_reportedAttached;
				_reportedAttached = true;
			}

			if (attached)
			{
				AttachmentChanged?.Invoke();
			}

			var notifications = new List<ShellNotification>();
			try
			{
				using var expiry = new CancellationTokenSource(hold, _time);
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
				while (notifications.Count == 0)
				{
					Collect(await _queue.Reader.ReadAsync(linked.Token), notifications);
				}
			}
			catch (OperationCanceledException)
			{
				return notifications;
			}

			while (_queue.Reader.TryRead(out var next))
			{
				Collect(next, notifications);
			}

			return notifications;
		}
		finally
		{
			Interlocked.Exchange(ref _waiting, 0);
		}
	}

	private void OnAttachmentWindowElapsed()
	{
		lock (_attachment)
		{
			var remaining = AttachmentWindow.Ticks
				- (_time.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastPollTicks));
			if (remaining > 0)
			{
				_detach.Change(TimeSpan.FromTicks(remaining), Timeout.InfiniteTimeSpan);
				return;
			}

			if (!_reportedAttached)
			{
				return;
			}

			_reportedAttached = false;
		}

		AttachmentChanged?.Invoke();
	}

	// A notification whose dispatcher already gave up has been shown by the platform fallback by now,
	// so handing it to the shell as well would show it twice. The lease restarts on the way out: it
	// bounds how long the shell may take to answer, not how long the notification sat in the queue
	// behind others - otherwise a batch would time out at its tail and be shown twice.
	private void Collect(ShellNotification notification, List<ShellNotification> notifications)
	{
		if (!_pending.TryGetValue(notification.Id, out var pending))
		{
			return;
		}

		try
		{
			pending.Lease.CancelAfter(Lease);
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		notifications.Add(notification);
	}

	private sealed record PendingNotification(TaskCompletionSource<bool> Result, CancellationTokenSource Lease);

	public void Report(long id, bool shown)
	{
		if (_pending.TryRemove(id, out var pending))
		{
			pending.Result.TrySetResult(shown);
		}
	}
}
