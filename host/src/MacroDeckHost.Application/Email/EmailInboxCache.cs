using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Email;
using Serilog;

namespace MacroDeckHost.Application.Email;

public interface IEmailInboxCache
{
	EmailInboxesSnapshot Snapshot { get; }

	DateTimeOffset? NextRetryAt { get; }

	event Action? Changed;

	Task SyncAsync(EmailSyncRequest request, CancellationToken cancellationToken);

	EmailInboxAggregate Aggregate(IReadOnlyCollection<string> inboxRefs);

	void RequestSync();
}

public interface IEmailEventPublisher
{
	void Publish(IReadOnlyList<EmailInboxChange> changes);
}

public sealed class EmailInboxCache : IEmailInboxCache, IDisposable
{
	public const int WindowSize = 20;

	public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

	private readonly IEmailInboxRegistry _registry;
	private readonly IEmailInboxKeyStore _keys;
	private readonly IEmailSyncSignal _signal;
	private readonly IEmailEventPublisher? _events;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _syncLock = new(1, 1);
	private readonly Dictionary<string, Tracking> _tracking = new(StringComparer.Ordinal);
	private readonly Lock _retryGate = new();

	private volatile EmailInboxesSnapshot _snapshot = EmailInboxesSnapshot.Empty;
	private DateTimeOffset? _nextRetryAt;

	public EmailInboxCache(
		IEmailInboxRegistry registry,
		IEmailInboxKeyStore keys,
		IEmailSyncSignal signal,
		TimeProvider time,
		ILogger logger,
		IEmailEventPublisher? events = null)
	{
		_registry = registry;
		_keys = keys;
		_signal = signal;
		_time = time;
		_events = events;
		_logger = logger.ForContext<EmailInboxCache>();
	}

	public EmailInboxesSnapshot Snapshot => _snapshot;

	public DateTimeOffset? NextRetryAt
	{
		get
		{
			lock (_retryGate)
			{
				return _nextRetryAt;
			}
		}
	}

	public event Action? Changed;

	public EmailInboxAggregate Aggregate(IReadOnlyCollection<string> inboxRefs)
	{
		return _snapshot.Aggregate(inboxRefs);
	}

	public void RequestSync()
	{
		_signal.RequestAll();
	}

	public async Task SyncAsync(EmailSyncRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			_registry.ReconcileSubscriptions();

			var previous = _snapshot;
			var now = _time.GetUtcNow();
			var inboxes = _registry.ResolveAll();
			var slugs = _keys.Assign([.. inboxes.Select(resolved => resolved.Inbox)]);
			var states = new List<EmailInboxState>(inboxes.Count);
			var changes = new List<EmailInboxChange>();

			foreach (var resolved in inboxes)
			{
				var inbox = resolved.Inbox;
				var slug = slugs.TryGetValue(inbox.Ref, out var assigned) ? assigned : EmailInboxKeys.FallbackSlug;
				var last = previous.Find(inbox.Ref);
				if (!_tracking.TryGetValue(inbox.Ref, out var tracking))
				{
					tracking = new Tracking();
					_tracking[inbox.Ref] = tracking;
				}

				var due = last is null || request.Covers(inbox) || tracking.RetryAt <= now;
				if (!due)
				{
					states.Add(last! with { Inbox = inbox, Slug = slug });
					continue;
				}

				var next = await ReadAsync(resolved, last, slug, tracking, now, cancellationToken)
					.ConfigureAwait(false);
				var detection = EmailChangeDetector.Detect(tracking.Baseline, next, tracking.Connected);
				tracking.Connected = detection.Connected;
				tracking.Baseline = detection.Baseline;
				changes.AddRange(detection.Changes);
				states.Add(next);
			}

			var live = states.Select(state => state.Ref).ToHashSet(StringComparer.Ordinal);
			foreach (var gone in _tracking.Keys.Where(inboxRef => !live.Contains(inboxRef)).ToList())
			{
				_tracking.Remove(gone);
			}

			lock (_retryGate)
			{
				_nextRetryAt = _tracking.Values.Select(entry => entry.RetryAt).Where(at => at is not null).Min();
			}

			var nextSnapshot = new EmailInboxesSnapshot([.. Order(states)]);
			_snapshot = nextSnapshot;

			if (!nextSnapshot.SameContentAs(previous))
			{
				RaiseChanged();
			}

			if (changes.Count > 0)
			{
				_events?.Publish(changes);
			}
		}
		finally
		{
			_syncLock.Release();
		}
	}

	public void Dispose()
	{
		_syncLock.Dispose();
	}

	private async Task<EmailInboxState> ReadAsync(
		ResolvedEmailInbox resolved,
		EmailInboxState? last,
		string slug,
		Tracking tracking,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		var inbox = resolved.Inbox;

		try
		{
			var read = await resolved.Provider
				.GetSnapshotAsync(inbox.LocalId, new EmailMessageQuery { MaxMessages = WindowSize }, cancellationToken)
				.ConfigureAwait(false);
			var latest = Normalize(read.Messages);
			var unreadCount = Math.Max(0, read.UnreadCount);

			var unread = latest.Where(message => !message.IsRead).ToList();
			IReadOnlyList<EmailMessage> latestUnread = unread;
			if (unreadCount > unread.Count)
			{
				var unreadRead = await resolved.Provider
					.GetSnapshotAsync(inbox.LocalId,
						new EmailMessageQuery { MaxMessages = WindowSize, UnreadOnly = true },
						cancellationToken)
					.ConfigureAwait(false);
				latestUnread = [.. Normalize(unreadRead.Messages).Where(message => !message.IsRead)];
			}

			tracking.Failures = 0;
			tracking.RetryAt = null;

			return new EmailInboxState
			{
				Inbox = inbox,
				Slug = slug,
				Status = read.Status,
				StatusMessage = EmailText.ClipOptional(read.StatusMessage, ProtocolLimits.MaxEmailSubjectLength),
				UnreadCount = unreadCount,
				TotalCount = Math.Max(0, read.TotalCount),
				Latest = latest,
				LatestUnread = latestUnread,
				SyncedAt = read.SyncedAt == default ? now : read.SyncedAt,
				IsStale = false
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			tracking.Failures++;
			_logger.Warning(exception,
				"Reading an email inbox of {IntegrationId} failed ({Failures} in a row); keeping its last data",
				inbox.IntegrationId,
				tracking.Failures);

			var kept = (last ?? new EmailInboxState { Inbox = inbox, Slug = slug }) with
			{
				Inbox = inbox, Slug = slug, IsStale = true
			};

			if (tracking.Failures == 1)
			{
				tracking.RetryAt = now + RetryDelay;
				return kept;
			}

			tracking.RetryAt = null;
			return kept with { Status = EmailInboxStatus.Disconnected, StatusMessage = null };
		}
	}

	private void RaiseChanged()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception exception)
		{
			_logger.Error(exception, "An email inbox change handler failed");
		}
	}

	private static IReadOnlyList<EmailMessage> Normalize(IEnumerable<EmailMessage>? messages)
	{
		return
		[
			.. (messages ?? [])
			.Where(message => message is not null && !string.IsNullOrEmpty(message.Id))
			.DistinctBy(message => message.Id, StringComparer.Ordinal)
			.Select(message => message with
			{
				SenderName = EmailText.ClipOptional(message.SenderName, ProtocolLimits.MaxEmailSenderLength),
				SenderAddress = EmailText.ClipOptional(message.SenderAddress, ProtocolLimits.MaxEmailSenderLength),
				Subject = EmailText.Clip(message.Subject?.Trim(), ProtocolLimits.MaxEmailSubjectLength),
				Preview = EmailText.ClipOptional(message.Preview, ProtocolLimits.MaxEmailPreviewLength)
			})
			.OrderByDescending(message => message.ReceivedAt)
			.Take(WindowSize)
		];
	}

	private static IEnumerable<EmailInboxState> Order(IEnumerable<EmailInboxState> states)
	{
		return states.OrderBy(state => state.Inbox.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(state => state.Ref, StringComparer.Ordinal);
	}

	private sealed class Tracking
	{
		public int Failures { get; set; }

		public DateTimeOffset? RetryAt { get; set; }

		public bool? Connected { get; set; }

		public EmailInboxState? Baseline { get; set; }
	}
}
