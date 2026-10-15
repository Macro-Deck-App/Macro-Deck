using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Imap;

namespace MacroDeckHost.Integrations.Imap.Connections;

internal sealed record ImapConnectionTimings(
	TimeSpan BackoffBase,
	TimeSpan BackoffCap,
	TimeSpan IdleRefresh,
	TimeSpan PollInterval)
{
	// RFC 2177: servers may drop an IDLE after 30 minutes, so it is renewed before that.
	public static ImapConnectionTimings Default { get; } = new(TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(60),
		TimeSpan.FromMinutes(25),
		TimeSpan.FromSeconds(60));
}

internal sealed class ImapAccountConnection : IAsyncDisposable
{
	private static readonly ILogger _logger = IntegrationLog.For<ImapAccountConnection>(ImapIntegration.IntegrationId);

	private readonly IImapSessionFactory _factory;
	private readonly ImapConnectionTimings _timings;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly CancellationTokenSource _stop = new();
	private readonly Lock _sync = new();
	private readonly Dictionary<(int Max, bool UnreadOnly), EmailInboxSnapshot> _fresh = [];
	private readonly Dictionary<(int Max, bool UnreadOnly), EmailInboxSnapshot> _known = [];

	private IImapSession? _session;
	private CancellationTokenSource? _idleDone;
	private CancellationTokenSource? _sessionLost;
	private TaskCompletionSource _noReaders = CompletedSignal();
	private int _readers;
	private long _generation;
	private EmailInboxStatus _status = EmailInboxStatus.Connecting;
	private ImapFailure? _failure;
	private Task? _loop;
	private int _disposed;

	public ImapAccountConnection(
		Guid entryId,
		string title,
		ImapAccountSettings settings,
		IImapSessionFactory factory,
		ImapConnectionTimings timings,
		TimeProvider time)
	{
		EntryId = entryId;
		Title = title;
		Settings = settings;
		_factory = factory;
		_timings = timings;
		_time = time;
	}

	public event EventHandler? Changed;

	public Guid EntryId { get; }

	public string Title { get; set; }

	public ImapAccountSettings Settings { get; }

	public string InboxId => Settings.InboxId;

	public EmailInboxStatus Status
	{
		get
		{
			lock (_sync)
			{
				return _status;
			}
		}
	}

	public ImapFailure? Failure
	{
		get
		{
			lock (_sync)
			{
				return _status == EmailInboxStatus.Connected ? null : _failure;
			}
		}
	}

	public void Start()
	{
		_loop ??= Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
	}

	public async Task<EmailInboxSnapshot> GetSnapshotAsync(EmailMessageQuery query, CancellationToken cancellationToken)
	{
		var key = (query.MaxMessages, query.UnreadOnly);
		CancellationTokenSource? idle;
		lock (_sync)
		{
			if (_fresh.TryGetValue(key, out var cached))
			{
				return cached;
			}

			if (_status != EmailInboxStatus.Connected)
			{
				return LastKnownLocked(query);
			}

			_readers++;
			if (_readers == 1)
			{
				_noReaders = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			idle = _idleDone;
		}

		try
		{
			EndIdle(idle);
			return await ReadAsync(query, key, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			lock (_sync)
			{
				if (--_readers == 0)
				{
					_noReaders.TrySetResult();
				}
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		await _stop.CancelAsync().ConfigureAwait(false);
		if (_loop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "The IMAP connection loop for {Account} ended with an error", Settings);
			}
		}

		_stop.Dispose();
	}

	private async Task<EmailInboxSnapshot> ReadAsync(
		EmailMessageQuery query,
		(int Max, bool UnreadOnly) key,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			long generation;
			IImapSession? session;
			lock (_sync)
			{
				if (_fresh.TryGetValue(key, out var cached))
				{
					return cached;
				}

				session = _status == EmailInboxStatus.Connected ? _session : null;
				if (session is null)
				{
					return LastKnownLocked(query);
				}

				generation = _generation;
			}

			try
			{
				var snapshot = await ReadSnapshotAsync(session, query, cancellationToken).ConfigureAwait(false);
				lock (_sync)
				{
					_known[key] = snapshot;
					if (_generation == generation && _status == EmailInboxStatus.Connected)
					{
						_fresh[key] = snapshot;
					}
				}

				return snapshot;
			}
			catch (ImapSessionException ex)
			{
				_logger.Information("Reading {Account} failed ({Failure}); reconnecting", Settings, ex.Failure);
				lock (_sync)
				{
					_sessionLost?.Cancel();
					return LastKnownLocked(query, EmailInboxStatus.Disconnected, ex.Failure);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<EmailInboxSnapshot> ReadSnapshotAsync(
		IImapSession session,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		var total = session.MessageCount;
		var unread = await session.SearchUnreadAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<ImapMessageSummary> summaries;
		if (query.UnreadOnly)
		{
			summaries = unread.Count == 0
				? []
				: await session.FetchAsync([.. unread.OrderDescending().Take(query.MaxMessages)], cancellationToken)
					.ConfigureAwait(false);
		}
		else
		{
			summaries = total == 0
				? []
				: await session.FetchLatestAsync(query.MaxMessages, cancellationToken).ConfigureAwait(false);
		}

		var validity = session.UidValidity;
		return new EmailInboxSnapshot
		{
			Status = EmailInboxStatus.Connected,
			UnreadCount = unread.Count,
			TotalCount = total,
			Messages =
			[
				.. summaries
					.OrderByDescending(summary => summary.Uid)
					.Take(query.MaxMessages)
					.Select(summary => new EmailMessage
					{
						Id = $"{validity}-{summary.Uid}",
						SenderName = summary.SenderName,
						SenderAddress = summary.SenderAddress,
						Subject = summary.Subject,
						Preview = summary.Preview,
						ReceivedAt = summary.ReceivedAt,
						IsRead = summary.IsSeen
					})
			],
			SyncedAt = _time.GetUtcNow()
		};
	}

	private EmailInboxSnapshot LastKnownLocked(
		EmailMessageQuery query,
		EmailInboxStatus? status = null,
		ImapFailure? failure = null)
	{
		var reported = status ?? _status;
		var message = StatusMessage(reported, status is null ? _failure : failure);

		if (_known.TryGetValue((query.MaxMessages, query.UnreadOnly), out var exact))
		{
			return exact with { Status = reported, StatusMessage = message };
		}

		if (_known.Values.MaxBy(snapshot => snapshot.SyncedAt) is { } latest)
		{
			return latest with
			{
				Status = reported,
				StatusMessage = message,
				Messages =
				[
					.. latest.Messages.Where(item => !query.UnreadOnly || !item.IsRead).Take(query.MaxMessages)
				]
			};
		}

		throw new InvalidOperationException($"IMAP inbox '{InboxId}' has no data yet; the connection is {reported}.");
	}

	private string? StatusMessage(EmailInboxStatus status, ImapFailure? failure)
	{
		return status switch
		{
			EmailInboxStatus.Connected => null,
			EmailInboxStatus.Connecting => ImapText.Resolve(Strings.Status.Connecting(Settings.Host)),
			EmailInboxStatus.Error => ImapText.Resolve(Strings.Status.SignInFailed()),
			_ => failure switch
			{
				ImapFailure.Certificate => ImapText.Resolve(Strings.Status.CertificateFailed(Settings.Host)),
				ImapFailure.FolderNotFound => ImapText.Resolve(Strings.Status.FolderNotFound(Settings.Folder)),
				_ => ImapText.Resolve(Strings.Status.Unreachable(Settings.Host))
			}
		};
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var failures = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			IImapSession? session = null;
			try
			{
				session = _factory.Create();
				await session.OpenAsync(Settings, cancellationToken).ConfigureAwait(false);
				await AttachAsync(session, cancellationToken).ConfigureAwait(false);
				failures = 0;
				_logger.Information("Connected to {Account}", Settings);
				SetStatus(EmailInboxStatus.Connected, null);
				await PumpAsync(session, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await DetachAsync(session).ConfigureAwait(false);
				return;
			}
			catch (ImapSessionException ex) when (ex.Failure == ImapFailure.Authentication)
			{
				_logger.Warning("The IMAP server refused the sign-in for {Account}; waiting for new credentials",
					Settings);
				await DetachAsync(session).ConfigureAwait(false);
				SetStatus(EmailInboxStatus.Error, ImapFailure.Authentication);
				return;
			}
			catch (Exception ex)
			{
				failures++;
				var failure = (ex as ImapSessionException)?.Failure ?? ImapFailure.Unreachable;
				_logger.Information("The connection to {Account} failed ({Failure}, {Failures} in a row): {Message}",
					Settings,
					failure,
					failures,
					ex.Message);
				await DetachAsync(session).ConfigureAwait(false);
				SetStatus(EmailInboxStatus.Disconnected, failure);
			}

			try
			{
				await Task.Delay(Backoff(failures), _time, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task AttachAsync(IImapSession session, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			session.MailboxChanged += OnMailboxChanged;
			lock (_sync)
			{
				_session = session;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task DetachAsync(IImapSession? session)
	{
		if (session is null)
		{
			return;
		}

		session.MailboxChanged -= OnMailboxChanged;
		await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
		try
		{
			lock (_sync)
			{
				if (ReferenceEquals(_session, session))
				{
					_session = null;
				}
			}

			await session.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Closing the IMAP session for {Account} failed", Settings);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task PumpAsync(IImapSession session, CancellationToken cancellationToken)
	{
		using var lost = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		lock (_sync)
		{
			_sessionLost = lost;
		}

		try
		{
			var token = lost.Token;
			while (true)
			{
				await WaitForReadersAsync(token).ConfigureAwait(false);
				await _gate.WaitAsync(token).ConfigureAwait(false);
				try
				{
					if (session.SupportsIdle)
					{
						await IdleOnceAsync(session, token).ConfigureAwait(false);
					}
					else
					{
						await session.NoOpAsync(token).ConfigureAwait(false);
					}
				}
				finally
				{
					_gate.Release();
				}

				if (!session.SupportsIdle)
				{
					await Task.Delay(_timings.PollInterval, _time, token).ConfigureAwait(false);
				}
			}
		}
		finally
		{
			lock (_sync)
			{
				_sessionLost = null;
			}
		}
	}

	private async Task IdleOnceAsync(IImapSession session, CancellationToken cancellationToken)
	{
		using var done = new CancellationTokenSource(_timings.IdleRefresh, _time);
		lock (_sync)
		{
			if (_readers > 0)
			{
				return;
			}

			_idleDone = done;
		}

		try
		{
			await session.IdleAsync(done.Token, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			lock (_sync)
			{
				_idleDone = null;
			}
		}
	}

	private static void EndIdle(CancellationTokenSource? idle)
	{
		try
		{
			idle?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The IDLE ended on its own between reading the source and cancelling it.
		}
	}

	private Task WaitForReadersAsync(CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			return _readers == 0 ? Task.CompletedTask : _noReaders.Task.WaitAsync(cancellationToken);
		}
	}

	private void OnMailboxChanged(object? sender, EventArgs e)
	{
		lock (_sync)
		{
			_fresh.Clear();
			_generation++;
		}

		RaiseChanged();
	}

	private void SetStatus(EmailInboxStatus status, ImapFailure? failure)
	{
		lock (_sync)
		{
			if (_status == status && _failure == failure && status != EmailInboxStatus.Connected)
			{
				return;
			}

			_status = status;
			_failure = failure;
			_fresh.Clear();
			_generation++;
		}

		RaiseChanged();
	}

	private void RaiseChanged()
	{
		try
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "An IMAP change subscriber failed");
		}
	}

	private TimeSpan Backoff(int failures)
	{
		var seconds = _timings.BackoffBase.TotalSeconds * Math.Pow(2, Math.Min(Math.Max(failures, 1), 8) - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _timings.BackoffCap.TotalSeconds));
	}

	private static TaskCompletionSource CompletedSignal()
	{
		var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		signal.SetResult();
		return signal;
	}
}
