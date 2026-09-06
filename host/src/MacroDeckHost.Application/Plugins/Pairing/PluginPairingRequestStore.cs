using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Application.Plugins.Pairing;

public sealed class PluginPairingRequestStore : IPluginPairingRequestStore
{
	private readonly TimeProvider _timeProvider;
	private readonly PluginPairingOptions _options;
	private readonly ConcurrentDictionary<string, PluginPairingRequestRecord> _records = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	public PluginPairingRequestStore(TimeProvider timeProvider, PluginPairingOptions options)
	{
		_timeProvider = timeProvider;
		_options = options;
	}

	public PluginPairingCreateResult Create(string pluginId,
		string displayName,
		string codeChallenge,
		PluginPairingClientInfo? client,
		bool arrivedOnPublicListener)
	{
		var now = _timeProvider.GetUtcNow();

		// The duplicate check, the capacity check and the insert all happen inside this one lock: doing
		// any of them outside it would let a concurrent burst of creates for the same (or different)
		// plugin id read a stale count and slip past the cap or the one-pending-per-plugin rule.
		lock (_lock)
		{
			PruneStaleEntries(now);

			if (_records.Values.Any(record => IsLive(record, now) &&
				string.Equals(record.PluginId, pluginId, StringComparison.Ordinal)))
			{
				// A pending request is never superseded or replaced, even by the same plugin id: letting
				// a fresh create silently cancel or swap a request the user can already see would let any
				// local process cancel a developer's visible approval prompt indefinitely, or swap a
				// hostile request in underneath the user's cursor between reading the prompt and clicking
				// Approve. The caller must wait for the existing request to resolve or expire.
				return PluginPairingCreateResult.Fail(PluginPairingCreateFailure.DuplicateRequest);
			}

			if (_records.Values.Count(record => IsLive(record, now)) >= _options.MaxPendingRequests)
			{
				return PluginPairingCreateResult.Fail(PluginPairingCreateFailure.CapacityExceeded);
			}

			var record = new PluginPairingRequestRecord
			{
				// TokenHasher.Generate() gives 32 random bytes, so this id is unguessable - but it is
				// deliberately NOT a credential. It authorises nothing by itself: only the code verifier,
				// which never travels with it, proves possession. That is exactly why it is safe to hand
				// back in a response body and carry in a URL (GET /{requestId}), even though it is shaped
				// like the secrets this same helper mints elsewhere.
				RequestId = TokenHasher.Generate(),
				PluginId = pluginId,
				DisplayName = displayName,
				CodeChallenge = codeChallenge,
				Client = client,
				CreatedAt = now,
				ExpiresAt = now + _options.RequestLifetime,
				State = PluginPairingRequestState.Pending,
				ReplaceExistingRegistration = false,
				ArrivedOnPublicListener = arrivedOnPublicListener
			};

			_records[record.RequestId] = record;

			return PluginPairingCreateResult.Success(record);
		}
	}

	public PluginPairingRequestRecord? Find(string requestId)
	{
		PruneStaleEntries(_timeProvider.GetUtcNow());

		return _records.TryGetValue(requestId, out var record) ? record : null;
	}

	public IReadOnlyList<PluginPairingRequestRecord> Snapshot()
	{
		PruneStaleEntries(_timeProvider.GetUtcNow());

		return _records.Values.ToList();
	}

	public bool Approve(string requestId, bool replaceExistingRegistration)
	{
		var now = _timeProvider.GetUtcNow();

		lock (_lock)
		{
			PruneStaleEntries(now);

			if (!_records.TryGetValue(requestId, out var record) ||
				record.State != PluginPairingRequestState.Pending)
			{
				return false;
			}

			record.State = PluginPairingRequestState.Approved;
			record.ReplaceExistingRegistration = replaceExistingRegistration;

			return true;
		}
	}

	public bool Reject(string requestId)
	{
		var now = _timeProvider.GetUtcNow();

		lock (_lock)
		{
			PruneStaleEntries(now);

			if (!_records.TryGetValue(requestId, out var record) ||
				record.State != PluginPairingRequestState.Pending)
			{
				return false;
			}

			record.State = PluginPairingRequestState.Rejected;

			return true;
		}
	}

	public PluginPairingRedeemResult TryRedeem(string requestId, string codeVerifier)
	{
		var now = _timeProvider.GetUtcNow();

		// Find, verify and (only on a successful verify) consume the request as one atomic unit under
		// the lock.
		//
		// The verifier is checked BEFORE the request is consumed: a wrong verifier must leave the
		// request untouched and still redeemable by whoever holds the correct one. If a wrong guess
		// consumed the request instead, anything that merely learned the (non-secret, URL-safe) request
		// id could permanently deny the legitimate plugin its approval - a denial-of-service that costs
		// the attacker nothing but a guess.
		//
		// Two concurrent redemptions with the CORRECT verifier must still yield exactly one winner. The
		// state check below - re-reading State inside this same lock and only transitioning out of
		// Approved once - is that compare-and-swap. It, not the verifier check, is the actual replay
		// defence: without it, two callers who both read "Approved" before either wrote back would both
		// proceed to register the plugin.
		lock (_lock)
		{
			PruneStaleEntries(now);

			if (!_records.TryGetValue(requestId, out var record))
			{
				return PluginPairingRedeemResult.Fail(PluginPairingRedeemFailure.NotFound);
			}

			if (record.State != PluginPairingRequestState.Approved)
			{
				return PluginPairingRedeemResult.Fail(PluginPairingRedeemFailure.NotApproved);
			}

			if (!PluginPairingCodeChallenge.Verify(codeVerifier, record.CodeChallenge))
			{
				return PluginPairingRedeemResult.Fail(PluginPairingRedeemFailure.VerificationFailed);
			}

			record.State = PluginPairingRequestState.Redeeming;

			return PluginPairingRedeemResult.Success(record);
		}
	}

	public void CompleteRedemption(string requestId, bool succeeded)
	{
		lock (_lock)
		{
			if (!_records.TryGetValue(requestId, out var record) ||
				record.State != PluginPairingRequestState.Redeeming)
			{
				return;
			}

			if (succeeded)
			{
				_records.TryRemove(requestId, out _);
			}
			else
			{
				// Terminal and deliberately not retryable: a failed database write after the verifier was
				// already consumed must fail closed rather than silently re-arm for another attempt. The
				// developer's fix is to start a new pairing request, not to retry this one.
				record.State = PluginPairingRequestState.Failed;
			}
		}
	}

	// Expiry is lazy - there is no background sweeper. Every public entry point prunes opportunistically
	// on its way in, exactly the way LoginThrottle.PruneStaleEntries does, so a request that nobody ever
	// polls again just falls out of the dictionary next time anything touches the store.
	private void PruneStaleEntries(DateTimeOffset now)
	{
		foreach (var (id, record) in _records)
		{
			if (now >= record.ExpiresAt)
			{
				_records.TryRemove(id, out _);
			}
		}
	}

	private static bool IsLive(PluginPairingRequestRecord record, DateTimeOffset now)
		=> now < record.ExpiresAt &&
			record.State
				is PluginPairingRequestState.Pending
				or PluginPairingRequestState.Approved
				or PluginPairingRequestState.Redeeming;
}
