using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Storage;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Protocol;
using MacroDeckHost.Integrations.Feeds.State;
using Serilog;

namespace MacroDeckHost.Integrations.Feeds.Connections;

internal delegate void FeedRefreshedHandler(FeedRuntime runtime, FeedSnapshot previous, FeedRefreshOutcome outcome);

internal sealed class FeedRuntime : IAsyncDisposable
{
	public static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);

	public static readonly TimeSpan MaxRetry = TimeSpan.FromHours(1);

	public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(6);

	private static readonly ILogger _logger = IntegrationLog.For<FeedRuntime>(FeedsIntegration.IntegrationId);

	private readonly IFeedFetcher _fetcher;
	private readonly IIntegrationStorage _storage;
	private readonly TimeProvider _time;
	private readonly FeedRefreshedHandler _refreshed;
	private readonly CancellationTokenSource _stop = new();
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly SemaphoreSlim _wake = new(0, 1);
	private readonly Lock _sync = new();

	private FeedStateDocument _document;
	private FeedSnapshot _snapshot;
	private TimeSpan? _retryAfter;
	private bool _writable = true;
	private Task? _loop;

	public FeedRuntime(
		FeedSettings settings,
		IFeedFetcher fetcher,
		IIntegrationStorage storage,
		TimeProvider time,
		FeedRefreshedHandler refreshed)
	{
		Settings = settings;
		_fetcher = fetcher;
		_storage = storage;
		_time = time;
		_refreshed = refreshed;
		_document = FeedStateUpdate.Fresh(settings);
		_snapshot = Build(_document);
	}

	public FeedSettings Settings { get; }

	public Guid EntryId => Settings.EntryId;

	public FeedSnapshot Snapshot
	{
		get
		{
			lock (_sync)
			{
				return _snapshot;
			}
		}
	}

	internal FeedStateDocument Document
	{
		get
		{
			lock (_sync)
			{
				return _document;
			}
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		FeedStateDocument? stored = null;
		try
		{
			(stored, _writable) = await FeedStateStore.LoadAsync(_storage, EntryId, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException)
		{
			_logger.Warning(exception, "The stored state of feed {Title} could not be read", Settings.Title);
		}

		var adopted = FeedStateUpdate.Adopt(stored, Settings);
		lock (_sync)
		{
			_document = adopted;
			_snapshot = Build(adopted);
		}
	}

	public void Start()
	{
		_loop ??= Task.Run(RunAsync);
	}

	public void RequestRefresh()
	{
		if (_wake.CurrentCount == 0)
		{
			try
			{
				_wake.Release();
			}
			catch (SemaphoreFullException)
			{
			}
		}
	}

	public async Task RefreshAsync(CancellationToken cancellationToken)
	{
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
		await EnterAsync(stopping.Token).ConfigureAwait(false);
		try
		{
			var current = Document;
			var request = new FeedRequest(Settings.Url, Settings.AllowLocalNetwork)
			{
				ETag = current.ETag,
				LastModified = current.LastModified
			};
			var fetch = await _fetcher.FetchAsync(request, stopping.Token).ConfigureAwait(false);
			var now = _time.GetUtcNow();
			_retryAfter = fetch.RetryAfter;

			var outcome = fetch.Status switch
			{
				FeedFetchStatus.Ok => Parse(current, fetch, now, stopping.Token),
				FeedFetchStatus.NotModified => FeedStateUpdate.ApplyNotModified(current, fetch, now),
				_ => FeedStateUpdate.ApplyFailure(current, fetch.Error ?? FeedErrorCodes.Network, now)
			};

			if (outcome.Document.Status == FeedStatus.Error)
			{
				_logger.Information("Refreshing feed {Title} failed: {Error}", Settings.Title, outcome.Document.Error);
			}

			await PersistAsync(outcome.Document).ConfigureAwait(false);
			Apply(outcome);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> MarkReadAsync(string key, CancellationToken cancellationToken)
	{
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
		await EnterAsync(stopping.Token).ConfigureAwait(false);
		try
		{
			var current = Document;
			var next = FeedStateUpdate.MarkRead(current, key);
			if (ReferenceEquals(next, current))
			{
				return false;
			}

			lock (_sync)
			{
				_document = next;
				_snapshot = Build(next);
			}

			await PersistAsync(next).ConfigureAwait(false);
			return true;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DeleteStateAsync(CancellationToken cancellationToken)
	{
		try
		{
			await FeedStateStore.DeleteAsync(_storage, EntryId, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException)
		{
			_logger.Warning(exception, "The stored state of feed {Title} could not be deleted", Settings.Title);
		}
	}

	public TimeSpan NextDelay()
	{
		var document = Document;
		var now = _time.GetUtcNow();
		DateTimeOffset due;

		if (document is { Status: FeedStatus.Error, Failures: > 0 })
		{
			var backoff = Backoff(document.Failures, _retryAfter);
			due = (document.LastAttempt ?? now) + backoff;
		}
		else if (document.LastUpdated is { } lastUpdated)
		{
			due = lastUpdated + Settings.RefreshInterval;
		}
		else
		{
			return TimeSpan.Zero;
		}

		return due > now ? due - now : TimeSpan.Zero;
	}

	public static TimeSpan Backoff(int failures, TimeSpan? retryAfter)
	{
		var exponent = Math.Clamp(failures - 1, 0, 10);
		var backoff = TimeSpan.FromTicks(Math.Min(FirstRetry.Ticks << exponent, MaxRetry.Ticks));
		if (retryAfter is { } requested)
		{
			var bounded = requested > MaxRetryAfter ? MaxRetryAfter : requested;
			return bounded > backoff ? bounded : backoff;
		}

		return backoff;
	}

	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync().ConfigureAwait(false);

		if (_loop is not null)
		{
			try
			{
				await _loop.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}

		await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
		_stop.Dispose();
		_gate.Dispose();
		_wake.Dispose();
	}

	private async Task EnterAsync(CancellationToken token)
	{
		await _gate.WaitAsync(token).ConfigureAwait(false);
		// A release can hand the gate to a waiter whose token was cancelled before it observed the cancellation.
		if (token.IsCancellationRequested)
		{
			_gate.Release();
			token.ThrowIfCancellationRequested();
		}
	}

	private FeedRefreshOutcome Parse(
		FeedStateDocument current,
		FeedFetchResult fetch,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		try
		{
			var parsed = FeedParser.Parse(fetch.Content ?? [], fetch.FinalUrl ?? Settings.Url, cancellationToken);
			return FeedStateUpdate.ApplyContent(current, parsed, Settings, fetch, now);
		}
		catch (FeedParseException)
		{
			return FeedStateUpdate.ApplyFailure(current, FeedErrorCodes.InvalidFeed, now);
		}
	}

	private void Apply(FeedRefreshOutcome outcome)
	{
		FeedSnapshot previous;
		lock (_sync)
		{
			previous = _snapshot;
			_document = outcome.Document;
			_snapshot = Build(outcome.Document);
		}

		_refreshed(this, previous, outcome);
	}

	private async Task PersistAsync(FeedStateDocument document)
	{
		if (!_writable)
		{
			return;
		}

		try
		{
			await FeedStateStore.SaveAsync(_storage, EntryId, document, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException
			or InvalidOperationException
			or UnauthorizedAccessException)
		{
			_logger.Warning(exception, "The state of feed {Title} could not be stored", Settings.Title);
		}
	}

	private FeedSnapshot Build(FeedStateDocument document)
	{
		return new FeedSnapshot(Settings.EntryId, Settings.Title, Settings.VariableKey)
		{
			Group = Settings.Group,
			AllowLocalNetwork = Settings.AllowLocalNetwork,
			FeedTitle = document.FeedTitle,
			Items = document.Items,
			Unread = document.Unread,
			Status = document.Status,
			Error = document.Error,
			LastUpdated = document.LastUpdated
		};
	}

	private async Task RunAsync()
	{
		var token = _stop.Token;

		while (!token.IsCancellationRequested)
		{
			var delay = NextDelay();
			try
			{
				if (delay > TimeSpan.Zero)
				{
					await WaitAsync(delay, token).ConfigureAwait(false);
				}

				await RefreshAsync(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
#pragma warning disable CA1031 // The poll loop must survive any single failed refresh.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Error(exception, "Refreshing feed {Title} failed", Settings.Title);
				try
				{
					await WaitAsync(FirstRetry, token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					return;
				}
			}
		}
	}

	private async Task WaitAsync(TimeSpan delay, CancellationToken token)
	{
		using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
		var wake = _wake.WaitAsync(waiting.Token);
		var elapsed = Task.Delay(delay, _time, waiting.Token);
		await Task.WhenAny(wake, elapsed).ConfigureAwait(false);
		await waiting.CancelAsync().ConfigureAwait(false);

		try
		{
			await Task.WhenAll(wake, elapsed).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}

		token.ThrowIfCancellationRequested();
	}
}
