using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Integrations;
using Microsoft.AspNetCore.DataProtection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Licensing;

public sealed class CompanionLicenseService : ICompanionLicenseService, IDisposable
{
	public const string TokenKey = "license.companionToken";
	public const string TrialsKey = "license.trials";
	public const int MaximumTrials = 1000;
	public const string RevokedTestIdsKey = "license.revokedTestIds";
	public const int MaximumRevokedTestIds = 100;
	public const string PendingProofsKey = "license.pendingProofs";
	public const int MaximumPendingProofs = 8;
	public const int MaximumPendingLegacyAppProofs = 2;
	public const string RefusedProofKeysKey = "license.refusedProofKeys";
	public const int MaximumRefusedProofKeys = 64;
	public const string PlatformRevokedIdsKey = "license.platformRevokedIds";

	// Above this many Platform ids a sync answer names only the ids that concern the connection, so it stays
	// under UiWebSocketProtocol.MaxMessageBytes; the Companion fetches the full list itself.
	public const int MaximumSyncedPlatformRevokedIds = 4000;

	public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
	public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(30);
	public static readonly TimeSpan PendingPurchaseLifetime = TimeSpan.FromDays(7);
	public static readonly TimeSpan ProofRefusalLifetime = TimeSpan.FromDays(1);
	public static readonly TimeSpan RevocationRefreshInterval = TimeSpan.FromHours(1);
	public static readonly TimeSpan RevocationRetryDelay = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan LegacyTransferWait = TimeSpan.FromSeconds(10);

	private const int MaximumTrialDeviceIdLength = 128;

	private static readonly HashSet<string> PurchaseRefusals = new(StringComparer.Ordinal)
	{
		"purchase-refunded", "purchase-revoked", "purchase-canceled", "license-revoked"
	};
	private const string ProofProtectorPurpose = "MacroDeck.CompanionLicense.PendingProof";

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IPlatformLicenseClient _platform;
	private readonly TestCompanionLicenseIssuer _testIssuer;
	private readonly CompanionLicenseTokens _tokens;
	private readonly CompanionDeviceRegistry _companions;
	private readonly IUiTransport _ui;
	private readonly IDataProtector _proofProtector;
	private readonly TimeProvider _time;
	private readonly Func<double> _random;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Channel<bool> _wake =
		Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
	private readonly Dictionary<string, string> _submitters = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<TransferWaiter>> _waiters = new(StringComparer.Ordinal);

	private HashSet<string>? _platformRevoked;
	private bool _platformRevokedFetched;
	private bool _revocationsApplied;
	private bool _companionSynced;
	private DateTimeOffset? _revocationDueAt;
	private int _revocationFailures;

	public CompanionLicenseService(IServiceScopeFactory scopeFactory,
		IPlatformLicenseClient platform,
		TestCompanionLicenseIssuer testIssuer,
		CompanionLicenseTokens tokens,
		CompanionDeviceRegistry companions,
		IUiTransport ui,
		IDataProtectionProvider dataProtection,
		TimeProvider time,
		ILogger logger)
		: this(scopeFactory, platform, testIssuer, tokens, companions, ui, dataProtection, time, Random.Shared.NextDouble, logger)
	{
	}

	internal CompanionLicenseService(IServiceScopeFactory scopeFactory,
		IPlatformLicenseClient platform,
		TestCompanionLicenseIssuer testIssuer,
		CompanionLicenseTokens tokens,
		CompanionDeviceRegistry companions,
		IUiTransport ui,
		IDataProtectionProvider dataProtection,
		TimeProvider time,
		Func<double> random,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_platform = platform;
		_testIssuer = testIssuer;
		_tokens = tokens;
		_companions = companions;
		_ui = ui;
		_proofProtector = dataProtection.CreateProtector(ProofProtectorPurpose);
		_time = time;
		_random = random;
		_logger = logger;
	}

	public async Task<SyncCompanionLicenseResponse> SyncAsync(string connectionId,
		SyncCompanionLicenseRequest request,
		CancellationToken cancellationToken)
	{
		if (!_companionSynced)
		{
			_companionSynced = true;
			Wake();
		}

		var license = await AdoptAsync(request.License, cancellationToken);
		var trialStartedAt = await TrialStartAsync(request.TrialDeviceId, request.TrialStarted, cancellationToken);
		if (license is null or { IsTest: true } && request.Proof is { } proof)
		{
			await QueueAsync(connectionId, proof, false, cancellationToken);
		}

		return new SyncCompanionLicenseResponse
		{
			License = license?.Token,
			TrialStartedAt = trialStartedAt,
			RevokedLicenseIds = await RevokedIdsForAsync(request.License, license, cancellationToken)
		};
	}

	public async Task<CompanionLicenseStatus> RevokeTestLicenseAsync(CancellationToken cancellationToken)
	{
		var revokedId = await LockedAsync(async preferences =>
			{
				var stored = await _tokens.VerifyAsync(await StoredTokenAsync(preferences), trustTestKey: true);
				if (stored is not { IsTest: true })
				{
					return null;
				}

				var revoked = await ReadRevokedAsync(preferences);
				revoked.Remove(stored.LicenseId);
				revoked.Add(stored.LicenseId);
				await preferences.SetValue(TokenKey, string.Empty);
				await preferences.SetValue(RevokedTestIdsKey,
					JsonSerializer.Serialize(revoked.TakeLast(MaximumRevokedTestIds)));
				return stored.LicenseId;
			},
			cancellationToken);

		if (revokedId is not null)
		{
			await _companions.SendLicenseRevokedAsync(new CompanionLicenseRevokedEvent { LicenseId = revokedId });
			await NotifyChangedAsync();
		}

		return await GetStatusAsync(cancellationToken);
	}

	public async Task<CompanionLicenseStatus> GetStatusAsync(CancellationToken cancellationToken)
	{
		var status = Status(await AdoptAsync(null, cancellationToken));
		await LockedAsync(async preferences =>
			{
				// Checked with the test key trusted: debug Companions keep trusting a test license whatever the
				// host's mode, so it has to stay revocable after developer mode is turned off.
				status.TestLicenseStored =
					await _tokens.VerifyAsync(await StoredTokenAsync(preferences), trustTestKey: true) is { IsTest: true };
				var pending = await ReadPendingAsync(preferences);
				status.IssuePending = pending.Count > 0;
				status.NextIssueAttemptAt = pending.Count > 0 ? pending.Min(entry => entry.NextAttemptAt) : null;
				return true;
			},
			cancellationToken);
		return status;
	}

	public async Task<CompanionLicenseStatus?> IssueTestLicenseAsync(CancellationToken cancellationToken)
	{
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			if (!await DeveloperModeAsync(scope.ServiceProvider))
			{
				return null;
			}
		}

		var license = await AdoptAsync(await _testIssuer.IssueAsync(cancellationToken), cancellationToken);
		if (license is not null)
		{
			await _companions.SendLicenseAsync(new CompanionLicenseEvent { License = license.Token }, null);
		}

		var status = Status(license);
		status.TestLicenseStored = license?.IsTest == true;
		return status;
	}

	public async Task<LegacyPurchaseTransferResult> TransferLegacyPurchaseAsync(CompanionLicenseProof proof,
		CancellationToken cancellationToken)
	{
		if (await AdoptAsync(null, cancellationToken) is { IsTest: false })
		{
			return new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.AlreadyTransferred);
		}

		var queued = await QueueAsync(null, proof, true, cancellationToken);
		if (queued.Waiter is not { } waiter)
		{
			return queued.Answer!;
		}

		try
		{
			return await waiter.Result.Task.WaitAsync(LegacyTransferWait, _time, cancellationToken);
		}
		catch (TimeoutException)
		{
			return new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Pending);
		}
		finally
		{
			lock (_waiters)
			{
				if (_waiters.TryGetValue(queued.Key!, out var waiters) && waiters.Remove(waiter) && waiters.Count == 0)
				{
					_waiters.Remove(queued.Key!);
				}
			}
		}
	}

	internal async Task<DateTimeOffset?> RunDueWorkAsync(CancellationToken cancellationToken)
	{
		await RefreshRevocationsAsync(cancellationToken);
		await IssueDueProofsAsync(cancellationToken);
		return await NextDueAsync(cancellationToken);
	}

	internal async Task WaitForWorkAsync(DateTimeOffset? due, CancellationToken cancellationToken)
	{
		using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var tasks = new List<Task> { _wake.Reader.WaitToReadAsync(wait.Token).AsTask() };
		if (due is { } dueAt)
		{
			var delay = dueAt - _time.GetUtcNow();
			tasks.Add(Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, _time, wait.Token));
		}

		await Task.WhenAny(tasks);
		await wait.CancelAsync();
		while (_wake.Reader.TryRead(out _))
		{
		}

		cancellationToken.ThrowIfCancellationRequested();
	}

	internal TimeSpan RetryDelay(int attempts, TimeSpan? retryAfter)
	{
		var exponent = Math.Clamp(attempts, 1, 20) - 1;
		var ceiling = Math.Min(MaximumRetryDelay.TotalSeconds, InitialRetryDelay.TotalSeconds * Math.Pow(2, exponent));
		var jittered = ceiling * (0.5 + 0.5 * Math.Clamp(_random(), 0, 1));
		var requested = Math.Clamp(retryAfter?.TotalSeconds ?? 0, 0, MaximumRetryDelay.TotalSeconds);
		return TimeSpan.FromSeconds(Math.Max(jittered, requested));
	}

	private async Task<QueueOutcome> QueueAsync(string? connectionId,
		CompanionLicenseProof proof,
		bool legacyApp,
		CancellationToken cancellationToken)
	{
		if (!PlatformLicenseClient.IsSupported(proof) ||
			legacyApp != (proof.Platform == CompanionLicenseSources.AppStoreLegacy) ||
			PurchaseKey(proof) is not { } key)
		{
			return new QueueOutcome(Rejected("unsupported-source"));
		}

		QueueOutcome outcome;
		try
		{
			var serialized = JsonSerializer.Serialize(proof);
			var proofHash = Hash(serialized);
			var protectedProof = _proofProtector.Protect(serialized);
			outcome = await LockedAsync(async preferences =>
				{
					var refused = await ReadRefusedAsync(preferences);
					if (refused.FirstOrDefault(entry => entry.Key == key || entry.Key == proofHash) is { } refusal)
					{
						return new QueueOutcome(Rejected(refusal.Code ?? "purchase-refused"));
					}

					var pending = await ReadPendingAsync(preferences);
					var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
					var index = pending.FindIndex(entry => entry.Key == key);
					var added = index < 0;
					var rescheduled = false;
					if (!added)
					{
						var entry = pending[index];
						if (entry.ProofHash != proofHash)
						{
							entry = entry with { Proof = protectedProof, ProofHash = proofHash };
							CompleteWaiters(key,
								waiter => waiter.ProofHash != proofHash,
								new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Pending));
						}

						if (legacyApp)
						{
							var due = Math.Max(Math.Min(entry.NextAttemptAt, now), entry.NotBefore ?? 0);
							rescheduled = due != entry.NextAttemptAt;
							entry = entry with { NextAttemptAt = due };
						}

						pending[index] = entry;
					}
					else
					{
						if (!MakeRoom(pending, legacyApp))
						{
							return new QueueOutcome(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Unavailable));
						}

						pending.Add(new PendingProof(key, protectedProof, 0, now, now, proofHash, legacyApp));
					}

					await WritePendingAsync(preferences, pending);
					if (connectionId is not null)
					{
						lock (_submitters)
						{
							_submitters[key] = connectionId;
						}
					}

					TransferWaiter? waiter = null;
					if (legacyApp)
					{
						waiter = new TransferWaiter(proofHash,
							new TaskCompletionSource<LegacyPurchaseTransferResult>(TaskCreationOptions
								.RunContinuationsAsynchronously));
						lock (_waiters)
						{
							if (!_waiters.TryGetValue(key, out var waiters))
							{
								_waiters[key] = waiters = [];
							}

							waiters.Add(waiter);
						}
					}

					return new QueueOutcome(null, key, waiter, added, rescheduled);
				},
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning("Queueing a Companion purchase proof for issuing failed: {Error}", ex.GetType().Name);
			return new QueueOutcome(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Unavailable));
		}

		if (outcome.Added || legacyApp && outcome.Key is not null)
		{
			Wake();
		}

		if (outcome.Added || outcome.Rescheduled)
		{
			await NotifyChangedAsync();
		}

		return outcome;
	}

	// An anonymous legacy app caller may only displace its own kind, so it can never push a Companion's proof out.
	private bool MakeRoom(List<PendingProof> pending, bool legacyApp)
	{
		PendingProof? evicted = null;
		if (legacyApp)
		{
			var legacyEntries = pending.Where(entry => entry.LegacyApp).ToList();
			if (legacyEntries.Count >= MaximumPendingLegacyAppProofs || pending.Count >= MaximumPendingProofs)
			{
				evicted = legacyEntries.MinBy(entry => entry.QueuedAt);
				if (evicted is null)
				{
					return false;
				}
			}
		}
		else if (pending.Count >= MaximumPendingProofs)
		{
			evicted = pending.MinBy(entry => entry.QueuedAt)!;
		}

		if (evicted is not null)
		{
			pending.Remove(evicted);
			lock (_submitters)
			{
				_submitters.Remove(evicted.Key);
			}

			CompleteWaiters(evicted.Key,
				_ => true,
				new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Unavailable));
		}

		return true;
	}

	private void CompleteWaiters(string key, Func<TransferWaiter, bool> match, LegacyPurchaseTransferResult result)
	{
		lock (_waiters)
		{
			if (!_waiters.TryGetValue(key, out var waiters))
			{
				return;
			}

			foreach (var waiter in waiters.Where(match))
			{
				waiter.Result.TrySetResult(result);
			}
		}
	}

	private void CompleteAllWaiters(string? transferredKey)
	{
		lock (_waiters)
		{
			foreach (var (key, waiters) in _waiters)
			{
				var result = new LegacyPurchaseTransferResult(key == transferredKey
					? LegacyPurchaseTransferStatus.Transferred
					: LegacyPurchaseTransferStatus.AlreadyTransferred);
				foreach (var waiter in waiters)
				{
					waiter.Result.TrySetResult(result);
				}
			}
		}
	}

	private static LegacyPurchaseTransferResult Rejected(string code)
		=> new(LegacyPurchaseTransferStatus.Rejected, code);

	private async Task IssueDueProofsAsync(CancellationToken cancellationToken)
	{
		for (var pass = 0; pass < MaximumPendingProofs * 2; pass++)
		{
			var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
			var (due, cleared) = await LockedAsync(async preferences =>
				{
					var pending = await ReadPendingAsync(preferences);
					if (pending.Count == 0)
					{
						return ((PendingProof?)null, false);
					}

					if (await _tokens.VerifyAsync(await StoredTokenAsync(preferences), trustTestKey: true) is
						{ IsTest: false })
					{
						await WritePendingAsync(preferences, []);
						CompleteAllWaiters(null);
						return (null, true);
					}

					return (pending.Where(entry => entry.NextAttemptAt <= now).MinBy(entry => entry.NextAttemptAt), false);
				},
				cancellationToken);
			if (cleared)
			{
				await NotifyChangedAsync();
			}

			if (due is null)
			{
				return;
			}

			CompanionLicenseProof? proof;
			try
			{
				proof = JsonSerializer.Deserialize<CompanionLicenseProof>(_proofProtector.Unprotect(due.Proof));
			}
			catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
			{
				proof = null;
			}

			if (proof is null)
			{
				_logger.Warning("A pending Companion purchase proof could not be read and was dropped");
				await SettleAsync(due, null, cancellationToken);
				CompleteWaiters(due.Key, waiter => waiter.ProofHash == due.ProofHash, Rejected("invalid-proof"));
				continue;
			}

			var result = await _platform.IssueCompanionLicenseAsync(proof, cancellationToken);
			await ApplyAsync(due, result, cancellationToken);
		}
	}

	private async Task ApplyAsync(PendingProof due,
		PlatformLicenseIssueResult result,
		CancellationToken cancellationToken)
	{
		switch (result)
		{
			case PlatformLicenseIssueResult.Issued issued:
				var license = await AdoptAsync(issued.License, cancellationToken, due);
				if (license is { IsTest: false } && license.Token != issued.License)
				{
					await SettleAsync(due, null, cancellationToken);
					CompleteWaiters(due.Key,
						_ => true,
						new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.AlreadyTransferred));
					return;
				}

				if (license?.Token != issued.License)
				{
					_logger.Warning("The Macro Deck Platform issued a Companion license this host does not accept");
					await SettleAsync(due, due.ProofHash, cancellationToken, code: "license-not-accepted");
					CompleteWaiters(due.Key, waiter => waiter.ProofHash == due.ProofHash, Rejected("license-not-accepted"));
					return;
				}

				_logger.Information("Stored Companion license {LicenseId} issued by the Macro Deck Platform", license!.LicenseId);
				string? submitter;
				lock (_submitters)
				{
					_submitters.Remove(due.Key, out submitter);
				}

				await SettleAsync(due, null, cancellationToken);
				CompleteWaiters(due.Key,
					_ => true,
					new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Transferred));
				await _companions.SendLicenseAsync(new CompanionLicenseEvent { License = license.Token }, submitter);
				return;
			case PlatformLicenseIssueResult.Refused refused:
				_logger.Information("The Macro Deck Platform refused a Companion purchase proof: {Code}", refused.Code);
				if (PurchaseRefusals.Contains(refused.Code))
				{
					await SettleAsync(due, due.Key, cancellationToken, permanent: true, code: refused.Code);
				}
				else
				{
					await SettleAsync(due, due.ProofHash, cancellationToken, code: refused.Code);
				}

				CompleteWaiters(due.Key, waiter => waiter.ProofHash == due.ProofHash, Rejected(refused.Code));
				return;
			case PlatformLicenseIssueResult.Retry retry:
				await RescheduleAsync(due, retry, cancellationToken);
				return;
		}
	}

	private async Task RescheduleAsync(PendingProof due,
		PlatformLicenseIssueResult.Retry retry,
		CancellationToken cancellationToken)
	{
		var now = _time.GetUtcNow();
		if (retry.PurchasePending &&
			now - DateTimeOffset.FromUnixTimeMilliseconds(due.QueuedAt) > PendingPurchaseLifetime)
		{
			_logger.Information("A Companion purchase the store still does not report as completed was dropped");
			await SettleAsync(due, due.ProofHash, cancellationToken, code: "purchase-not-completed");
			CompleteWaiters(due.Key, waiter => waiter.ProofHash == due.ProofHash, Rejected("purchase-not-completed"));
			return;
		}

		var changed = await LockedAsync(async preferences =>
			{
				var pending = await ReadPendingAsync(preferences);
				var index = pending.FindIndex(entry => entry.Key == due.Key);
				if (index < 0)
				{
					return false;
				}

				var entry = pending[index];
				var attempts = entry.Attempts + 1;
				var next = (now + RetryDelay(attempts, retry.RetryAfter)).ToUnixTimeMilliseconds();
				long? notBefore = retry.RetryAfter is { } retryAfter
					? (now + TimeSpan.FromSeconds(Math.Clamp(retryAfter.TotalSeconds, 0, MaximumRetryDelay.TotalSeconds)))
					.ToUnixTimeMilliseconds()
					: null;
				if (entry.Proof != due.Proof)
				{
					next = Math.Max(Math.Min(entry.NextAttemptAt, next), notBefore ?? 0);
				}

				pending[index] = entry with { Attempts = attempts, NextAttemptAt = next, NotBefore = notBefore };
				await WritePendingAsync(preferences, pending);
				return true;
			},
			cancellationToken);
		CompleteWaiters(due.Key,
			waiter => waiter.ProofHash == due.ProofHash,
			new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Pending, retry.Code));
		if (changed)
		{
			await NotifyChangedAsync();
		}
	}

	// A retry reschedules the purchase, but a result only settles the proof it was computed for: a proof
	// resent for the same purchase during the call keeps its entry.
	private async Task SettleAsync(PendingProof due,
		string? refusedKey,
		CancellationToken cancellationToken,
		bool permanent = false,
		string? code = null)
	{
		var now = _time.GetUtcNow();
		var removed = await LockedAsync(async preferences =>
			{
				var pending = await ReadPendingAsync(preferences);
				var removed = pending.RemoveAll(entry => entry.Key == due.Key && entry.Proof == due.Proof) > 0;
				if (removed)
				{
					await WritePendingAsync(preferences, pending);
					lock (_submitters)
					{
						_submitters.Remove(due.Key);
					}
				}

				if (!string.IsNullOrEmpty(refusedKey))
				{
					var refused = await ReadRefusedAsync(preferences);
					refused.RemoveAll(entry => entry.Key == refusedKey || entry.ExpiresAt <= now.ToUnixTimeMilliseconds());
					refused.Add(new RefusedProof(refusedKey,
						permanent ? null : (now + ProofRefusalLifetime).ToUnixTimeMilliseconds(),
						code));
					while (refused.Count > MaximumRefusedProofKeys)
					{
						refused.RemoveAt(Math.Max(0, refused.FindIndex(entry => entry.ExpiresAt is not null)));
					}

					await preferences.SetValue(RefusedProofKeysKey, JsonSerializer.Serialize(refused));
				}

				return removed;
			},
			cancellationToken);
		if (removed)
		{
			await NotifyChangedAsync();
		}
	}

	private async Task RefreshRevocationsAsync(CancellationToken cancellationToken)
	{
		if (!_revocationsApplied)
		{
			_revocationsApplied = true;
			await ApplyRevocationsAsync(cancellationToken);
		}

		var now = _time.GetUtcNow();
		if (_revocationDueAt > now || !await LicensingInUseAsync(cancellationToken))
		{
			return;
		}

		var ids = await _platform.GetRevokedLicenseIdsAsync(cancellationToken);
		if (ids is null)
		{
			_revocationFailures++;
			var backoff = RevocationRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(_revocationFailures, 10) - 1);
			_revocationDueAt = now + TimeSpan.FromSeconds(Math.Min(backoff, RevocationRefreshInterval.TotalSeconds));
			return;
		}

		_revocationFailures = 0;
		_revocationDueAt = now + RevocationRefreshInterval;
		await LockedAsync(async preferences =>
			{
				_platformRevoked = new HashSet<string>(ids, StringComparer.Ordinal);
				_platformRevokedFetched = true;
				await preferences.SetValue(PlatformRevokedIdsKey,
					JsonSerializer.Serialize(new PlatformRevocations(ids, now.ToUnixTimeMilliseconds())));
				return true;
			},
			cancellationToken);
		await ApplyRevocationsAsync(cancellationToken);
	}

	private async Task ApplyRevocationsAsync(CancellationToken cancellationToken)
	{
		var revokedId = await LockedAsync(async preferences =>
			{
				var stored = await _tokens.VerifyAsync(await StoredTokenAsync(preferences), trustTestKey: true);
				if (stored is null || !(await PlatformRevokedAsync(preferences)).Contains(stored.LicenseId))
				{
					return null;
				}

				await preferences.SetValue(TokenKey, string.Empty);
				return stored.LicenseId;
			},
			cancellationToken);
		if (revokedId is null)
		{
			return;
		}

		_logger.Information("Removed Companion license {LicenseId}, which the Macro Deck Platform revoked", revokedId);
		await _companions.SendLicenseRevokedAsync(new CompanionLicenseRevokedEvent { LicenseId = revokedId });
		await NotifyChangedAsync();
	}

	private Task<bool> LicensingInUseAsync(CancellationToken cancellationToken)
		=> LockedAsync(async preferences =>
				_companionSynced ||
				_platformRevokedFetched ||
				!string.IsNullOrEmpty(await StoredTokenAsync(preferences)) ||
				(await ReadPendingAsync(preferences)).Count > 0,
			cancellationToken);

	private async Task<DateTimeOffset?> NextDueAsync(CancellationToken cancellationToken)
	{
		var nextAttempt = await LockedAsync(async preferences =>
			{
				var pending = await ReadPendingAsync(preferences);
				return pending.Count > 0 ? pending.Min(entry => entry.NextAttemptAt) : (long?)null;
			},
			cancellationToken);
		DateTimeOffset? next = nextAttempt is { } attempt ? DateTimeOffset.FromUnixTimeMilliseconds(attempt) : null;
		if (await LicensingInUseAsync(cancellationToken))
		{
			var revocation = _revocationDueAt ?? _time.GetUtcNow();
			next = next is { } due && due < revocation ? due : revocation;
		}

		return next;
	}

	private async Task<List<string>> RevokedIdsForAsync(string? submitted,
		CompanionLicense? stored,
		CancellationToken cancellationToken)
	{
		var submittedId = (await _tokens.VerifyAsync(submitted, trustTestKey: true))?.LicenseId;
		return await LockedAsync(async preferences =>
			{
				var revoked = await ReadRevokedAsync(preferences);
				var platform = await PlatformRevokedAsync(preferences);
				IEnumerable<string> synced = platform.Count <= MaximumSyncedPlatformRevokedIds
					? platform
					: new[] { submittedId, stored?.LicenseId }.OfType<string>().Where(platform.Contains);
				return revoked.Concat(synced).Distinct(StringComparer.Ordinal).ToList();
			},
			cancellationToken);
	}

	// The stored token is verified against the current trust set on every read, so a test license
	// stops being handed out as soon as developer mode is turned off.
	private async Task<CompanionLicense?> AdoptAsync(string? candidate,
		CancellationToken cancellationToken,
		PendingProof? issuedFor = null)
	{
		bool developerMode;
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			developerMode = await DeveloperModeAsync(scope.ServiceProvider);
		}

		var adoptedNew = false;
		var license = await LockedAsync(async preferences =>
			{
				var stored = await _tokens.VerifyAsync(await StoredTokenAsync(preferences), developerMode);
				var adopted = await _tokens.VerifyAsync(candidate, developerMode);
				if (adopted is null ||
					stored is not null && (!stored.IsTest || adopted.IsTest) ||
					(await ReadRevokedAsync(preferences)).Contains(adopted.LicenseId) ||
					(await PlatformRevokedAsync(preferences)).Contains(adopted.LicenseId))
				{
					return stored;
				}

				await preferences.SetValue(TokenKey, adopted.Token);
				if (!adopted.IsTest)
				{
					if ((await ReadPendingAsync(preferences)).Count > 0)
					{
						await WritePendingAsync(preferences, []);
					}

					CompleteAllWaiters(issuedFor?.Key);
				}

				adoptedNew = true;
				return adopted;
			},
			cancellationToken);
		if (adoptedNew)
		{
			await NotifyChangedAsync();
		}

		return license;
	}

	private async Task<long?> TrialStartAsync(string? trialDeviceId,
		bool trialStarted,
		CancellationToken cancellationToken)
	{
		if (trialDeviceId is not { Length: > 0 and <= MaximumTrialDeviceIdLength })
		{
			return null;
		}

		return await LockedAsync(async preferences =>
			{
				var trials = ReadTrials((await preferences.GetByKey(TrialsKey))?.Value);
				if (trials.TryGetValue(trialDeviceId, out var known))
				{
					return known;
				}

				if (!trialStarted)
				{
					return (long?)null;
				}

				var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
				trials[trialDeviceId] = now;
				// A ceiling on growth, not abuse protection: the oldest start goes first. A central trial
				// record belongs to the Platform API.
				foreach (var oldest in trials.OrderBy(entry => entry.Value)
					.Take(trials.Count - MaximumTrials)
					.Select(entry => entry.Key)
					.ToList())
				{
					trials.Remove(oldest);
				}

				await preferences.SetValue(TrialsKey, JsonSerializer.Serialize(trials));
				return now;
			},
			cancellationToken);
	}

	private async Task<T> LockedAsync<T>(Func<IAppPreferenceRepository, Task<T>> body,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await body(preferences);
		}
		finally
		{
			_gate.Release();
		}
	}

	private void Wake() => _wake.Writer.TryWrite(true);

	private async Task NotifyChangedAsync()
	{
		try
		{
			await _ui.SendToGroup(UiAdminGroups.Admin, new CompanionLicenseChangedEvent());
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not tell the admin UI that the Companion license changed");
		}
	}

	private static async Task<string?> StoredTokenAsync(IAppPreferenceRepository preferences)
		=> (await preferences.GetByKey(TokenKey))?.Value;

	private async Task<HashSet<string>> PlatformRevokedAsync(IAppPreferenceRepository preferences)
	{
		if (_platformRevoked is not null)
		{
			return _platformRevoked;
		}

		var cached = ReadJson<PlatformRevocations>((await preferences.GetByKey(PlatformRevokedIdsKey))?.Value);
		_platformRevokedFetched = cached is not null;
		_platformRevoked = new HashSet<string>(cached?.Ids ?? [], StringComparer.Ordinal);
		return _platformRevoked;
	}

	private static async Task<List<string>> ReadRevokedAsync(IAppPreferenceRepository preferences)
		=> ReadJson<List<string>>((await preferences.GetByKey(RevokedTestIdsKey))?.Value) ?? [];

	private async Task<List<RefusedProof>> ReadRefusedAsync(IAppPreferenceRepository preferences)
	{
		var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
		return (ReadJson<List<RefusedProof>>((await preferences.GetByKey(RefusedProofKeysKey))?.Value) ?? [])
			.Where(entry => entry is { Key.Length: > 0 } && (entry.ExpiresAt is null || entry.ExpiresAt > now))
			.ToList();
	}

	private static async Task<List<PendingProof>> ReadPendingAsync(IAppPreferenceRepository preferences)
		=> (ReadJson<List<PendingProof>>((await preferences.GetByKey(PendingProofsKey))?.Value) ?? [])
			.Where(entry => entry is { Key.Length: > 0, Proof.Length: > 0 })
			.ToList();

	private static Task WritePendingAsync(IAppPreferenceRepository preferences, List<PendingProof> pending)
		=> preferences.SetValue(PendingProofsKey, JsonSerializer.Serialize(pending));

	private static T? ReadJson<T>(string? json)
		where T : class
	{
		if (string.IsNullOrEmpty(json))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<T>(json);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	// The Platform keys a purchase by its purchase token or App Store (app) transaction id. The id is read
	// from the unverified JWS only to recognise the same purchase here; the Platform verifies the signature.
	private static string? PurchaseKey(CompanionLicenseProof proof)
	{
		var id = proof.Platform switch
		{
			CompanionLicenseSources.AppStore => PayloadClaim(proof.SignedPayload, "originalTransactionId") ??
				proof.TransactionId,
			CompanionLicenseSources.AppStoreLegacy => PayloadClaim(proof.SignedPayload, "appTransactionId") ??
				(string.IsNullOrEmpty(proof.SignedPayload) ? null : Hash(proof.SignedPayload)),
			_ => proof.PurchaseToken
		};
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		return Hash($"{proof.Platform}:{id}");
	}

	private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

	private static string? PayloadClaim(string? signedPayload, string claim)
	{
		var parts = signedPayload?.Split('.');
		if (parts is not { Length: 3 })
		{
			return null;
		}

		try
		{
			using var payload = JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]));
			return payload.RootElement.ValueKind == JsonValueKind.Object &&
				payload.RootElement.TryGetProperty(claim, out var id)
					? id.ValueKind switch
					{
						JsonValueKind.String => id.GetString(),
						JsonValueKind.Number => id.GetRawText(),
						_ => null
					}
					: null;
		}
		catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
		{
			return null;
		}
	}

	private static Dictionary<string, long> ReadTrials(string? json)
		=> new(ReadJson<Dictionary<string, long>>(json) ?? [], StringComparer.Ordinal);

	public void Dispose() => _gate.Dispose();

	private static async Task<bool> DeveloperModeAsync(IServiceProvider services)
		=> (await services.GetRequiredService<IAppPreferenceService>().GetDeveloper()).Enabled;

	private static CompanionLicenseStatus Status(CompanionLicense? license)
		=> license is null
			? new CompanionLicenseStatus()
			: new CompanionLicenseStatus
			{
				Licensed = true,
				LicenseId = license.LicenseId,
				Source = license.Source,
				KeyId = license.KeyId,
				IssuedAt = license.IssuedAt.ToUnixTimeMilliseconds(),
				PurchasedAt = license.PurchasedAt?.ToUnixTimeMilliseconds(),
				BillingId = license.BillingId,
				IsTest = license.IsTest
			};

	internal sealed record PendingProof(
		string Key,
		string Proof,
		int Attempts,
		long NextAttemptAt,
		long QueuedAt,
		string? ProofHash = null,
		bool LegacyApp = false,
		long? NotBefore = null);

	private sealed record RefusedProof(string Key, long? ExpiresAt, string? Code = null);

	private sealed record TransferWaiter(string ProofHash, TaskCompletionSource<LegacyPurchaseTransferResult> Result);

	private sealed record QueueOutcome(
		LegacyPurchaseTransferResult? Answer,
		string? Key = null,
		TransferWaiter? Waiter = null,
		bool Added = false,
		bool Rescheduled = false);

	private sealed record PlatformRevocations(IReadOnlyList<string> Ids, long FetchedAt);
}
