using System.Threading.Channels;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Licensing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Licensing;

public sealed class CompanionLicenseAccountSyncBackgroundService : BackgroundService
{
	internal static readonly TimeSpan PollWait = TimeSpan.FromSeconds(50);
	internal static readonly TimeSpan MinimumPollGap = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan MaximumPollJitter = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);
	internal static readonly TimeSpan BlockedRetryDelay = TimeSpan.FromMinutes(30);

	private const string LicenseRevoked = "license-revoked";

	private readonly IConnectSessionService _session;
	private readonly CompanionLicenseService _licenses;
	private readonly IPlatformLicenseAccountClient _client;
	private readonly TimeProvider _time;
	private readonly Func<double> _random;
	private readonly ILogger _logger;
	private readonly Channel<bool> _signals =
		Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
	private readonly Lock _lock = new();

	private CancellationTokenSource? _cycle;
	private ConnectSessionSnapshot? _observed;
	private int _failures;
	private DateTimeOffset? _retryAt;
	private string? _retrySubject;
	private (string Subject, DateTimeOffset Until)? _uploadBlocked;
	private UploadAttempt? _lastUpload;
	private string? _cycleSubject;

	public CompanionLicenseAccountSyncBackgroundService(IConnectSessionService session,
		CompanionLicenseService licenses,
		IPlatformLicenseAccountClient client,
		TimeProvider time,
		ILogger logger)
		: this(session, licenses, client, time, Random.Shared.NextDouble, logger)
	{
	}

	internal CompanionLicenseAccountSyncBackgroundService(IConnectSessionService session,
		CompanionLicenseService licenses,
		IPlatformLicenseAccountClient client,
		TimeProvider time,
		Func<double> random,
		ILogger logger)
	{
		_session = session;
		_licenses = licenses;
		_client = client;
		_time = time;
		_random = random;
		_logger = logger.ForContext<CompanionLicenseAccountSyncBackgroundService>();
	}

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		_session.SessionChanged += OnSessionChanged;
		_licenses.LicenseChanged += OnLicenseChanged;
		lock (_lock)
		{
			_observed = _session.Current;
		}

		_signals.Writer.TryWrite(true);
		return base.StartAsync(cancellationToken);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_session.SessionChanged -= OnSessionChanged;
		_licenses.LicenseChanged -= OnLicenseChanged;
		await base.StopAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			while (_signals.Reader.TryRead(out _))
			{
			}

			try
			{
				await RunOnceAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Syncing the Companion license with the Macro Deck account failed");
				ScheduleRetry(null, false, _session.Current.Account?.Subject);
			}
		}
	}

	private async Task RunOnceAsync(CancellationToken stoppingToken)
	{
		var snapshot = _session.Current;
		var retryAt = RetryAtFor(snapshot.Account?.Subject);
		if (retryAt > _time.GetUtcNow())
		{
			await WaitForSignalAsync(retryAt, stoppingToken);
			return;
		}

		if (snapshot.Status != ConnectAccountStatus.SignedIn || snapshot.Account is not { } account)
		{
			_cycleSubject = null;
			if (snapshot.Status == ConnectAccountStatus.SignedOut)
			{
				await _licenses.ReportAccountSignedOutAsync(stoppingToken);
			}
			else
			{
				await _licenses.ReportAccountUnknownAsync(stoppingToken);
			}

			await WaitForSignalAsync(null, stoppingToken);
			return;
		}

		if (_cycleSubject != account.Subject)
		{
			_cycleSubject = account.Subject;
			await _licenses.ReportAccountUnknownAsync(stoppingToken);
		}

		using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		lock (_lock)
		{
			_cycle = cycle;
		}

		try
		{
			if (_session.Current is { Status: ConnectAccountStatus.SignedIn, Account.Subject: var current } &&
				current == account.Subject)
			{
				await SyncAsync(account.Subject, cycle.Token);
			}
		}
		catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			lock (_lock)
			{
				_cycle = null;
			}
		}
	}

	private async Task SyncAsync(string subject, CancellationToken cancellationToken)
	{
		long? revision = null;
		while (true)
		{
			var requested = revision is null ? TimeSpan.Zero : PollWait;
			var started = _time.GetUtcNow();
			var answer = await _client.GetAccountLicenseAsync(revision, PollWait, cancellationToken);
			if (answer is PlatformAccountLicenseResult.SignedOut)
			{
				await WaitForSignalAsync(null, cancellationToken);
				return;
			}

			if (answer is not PlatformAccountLicenseResult.Current current)
			{
				Fail(subject, answer);
				return;
			}

			var elapsed = _time.GetUtcNow() - started;
			lock (_lock)
			{
				_failures = 0;
			}

			var unchanged = revision == current.Revision;
			revision = current.Revision;

			var reconcile = await _licenses.ReconcileAccountAsync(current.License, cancellationToken);
			if (reconcile is { UploadToken: { } token, UploadLicenseId: { } licenseId })
			{
				var attempt = new UploadAttempt(subject, licenseId, current.Revision, current.License);
				if (_lastUpload != attempt && !UploadBlocked(subject))
				{
					var stored = await UploadAsync(attempt, token, cancellationToken);
					if (stored is null)
					{
						return;
					}

					revision = stored.Value;
					continue;
				}
			}

			var delay = TimeSpan.Zero;
			if (current.RetryAfter is { } retryAfter)
			{
				delay = Clamp(retryAfter, MaximumRetryDelay);
			}
			else if (requested > TimeSpan.Zero && unchanged)
			{
				if (elapsed < requested && elapsed < MinimumPollGap)
				{
					delay = MinimumPollGap - elapsed;
				}

				delay += MaximumPollJitter * Math.Clamp(_random(), 0, 1);
			}

			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay, _time, cancellationToken);
			}
		}
	}

	private async Task<long?> UploadAsync(UploadAttempt attempt, string token, CancellationToken cancellationToken)
	{
		var answer = await _client.StoreAccountLicenseAsync(token, cancellationToken);
		switch (answer)
		{
			case PlatformAccountLicenseResult.Current stored:
				_lastUpload = attempt with { Revision = stored.Revision, AccountLicense = stored.License };
				if (stored.Revision != attempt.Revision)
				{
					_logger.Information("Saved Companion license {LicenseId} to the Macro Deck account", attempt.LicenseId);
				}

				await _licenses.ReconcileAccountAsync(stored.License, cancellationToken);
				return stored.Revision;
			case PlatformAccountLicenseResult.Conflict conflict:
				_lastUpload = attempt with { Revision = conflict.Revision, AccountLicense = conflict.License };
				await _licenses.ReconcileAccountAsync(conflict.License, cancellationToken);
				return conflict.Revision;
			case PlatformAccountLicenseResult.Refused refused:
				_lastUpload = attempt;
				_logger.Information("The Macro Deck Platform refused to save Companion license {LicenseId} to the account: {Code}",
					attempt.LicenseId,
					refused.Code);
				if (refused.Code == LicenseRevoked)
				{
					await _licenses.DropRevokedLicenseAsync(attempt.LicenseId, cancellationToken);
				}

				return attempt.Revision;
			case PlatformAccountLicenseResult.Unavailable { AccountBlocked: true }:
				_uploadBlocked = (attempt.Subject, _time.GetUtcNow() + BlockedRetryDelay);
				return attempt.Revision;
			case PlatformAccountLicenseResult.SignedOut:
				await WaitForSignalAsync(null, cancellationToken);
				return null;
			default:
				Fail(attempt.Subject, answer);
				return null;
		}
	}

	private void Fail(string subject, PlatformAccountLicenseResult answer)
	{
		var unavailable = answer as PlatformAccountLicenseResult.Unavailable;
		ScheduleRetry(unavailable?.RetryAfter, unavailable?.AccountBlocked == true, subject);
	}

	private bool UploadBlocked(string subject)
		=> _uploadBlocked is { } blocked && blocked.Subject == subject && blocked.Until > _time.GetUtcNow();

	private DateTimeOffset? RetryAtFor(string? subject)
	{
		lock (_lock)
		{
			return _retrySubject == subject ? _retryAt : null;
		}
	}

	private void ScheduleRetry(TimeSpan? retryAfter, bool blocked, string? subject)
	{
		lock (_lock)
		{
			_failures++;
			var ceiling = blocked
				? BlockedRetryDelay
				: TimeSpan.FromSeconds(Math.Min(MaximumRetryDelay.TotalSeconds,
					InitialRetryDelay.TotalSeconds * Math.Pow(2, Math.Clamp(_failures, 1, 20) - 1)));
			var delay = ceiling * (0.5 + 0.5 * Math.Clamp(_random(), 0, 1));
			if (retryAfter is { } requested)
			{
				delay = TimeSpan.FromTicks(Math.Max(delay.Ticks, Clamp(requested, BlockedRetryDelay).Ticks));
			}

			_retryAt = _time.GetUtcNow() + delay;
			_retrySubject = subject;
		}
	}

	private async Task WaitForSignalAsync(DateTimeOffset? until, CancellationToken cancellationToken)
	{
		using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var tasks = new List<Task> { _signals.Reader.WaitToReadAsync(wait.Token).AsTask() };
		if (until is { } due)
		{
			var delay = due - _time.GetUtcNow();
			tasks.Add(Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, _time, wait.Token));
		}

		await Task.WhenAny(tasks);
		await wait.CancelAsync();
		cancellationToken.ThrowIfCancellationRequested();
	}

	private void OnSessionChanged(object? sender, ConnectSessionSnapshot snapshot)
	{
		lock (_lock)
		{
			var previous = _observed;
			_observed = snapshot;
			var accountChanged = previous is null ||
				previous.Status != snapshot.Status ||
				previous.Account?.Subject != snapshot.Account?.Subject;
			var backOnline = previous is { Connectivity: ConnectConnectivity.Offline } &&
				snapshot.Connectivity == ConnectConnectivity.Ok;
			if (!accountChanged && !backOnline)
			{
				return;
			}

			_retryAt = null;
			_failures = 0;
			if (accountChanged)
			{
				_lastUpload = null;
				_uploadBlocked = null;
			}
		}

		Restart();
	}

	private void OnLicenseChanged(object? sender, EventArgs e) => Restart();

	private void Restart()
	{
		lock (_lock)
		{
			_cycle?.Cancel();
		}

		_signals.Writer.TryWrite(true);
	}

	private static TimeSpan Clamp(TimeSpan value, TimeSpan maximum)
		=> value < TimeSpan.Zero ? TimeSpan.Zero : value > maximum ? maximum : value;

	private sealed record UploadAttempt(string Subject, string LicenseId, long Revision, string? AccountLicense);
}
