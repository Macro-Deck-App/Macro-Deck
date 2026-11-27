using System.Security.Cryptography;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public sealed record KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome Outcome, string? Reason);

public static class KeyRingKeystoreRead
{
	// The only place that sees the key bytes of a read made while locked: it wipes them, so several
	// observers of one read can never race on a buffer one of them already cleared.
	public static Task<KeyRingKeystoreReadOutcome> Evaluate(Task<KekStoreReadResult> read, string? ringKekId)
	{
		return read.ContinueWith(completed => Classify(completed, ringKekId),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private static KeyRingKeystoreReadOutcome Classify(Task<KekStoreReadResult> completed, string? ringKekId)
	{
		if (!completed.IsCompletedSuccessfully)
		{
			return new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.Unavailable,
				completed.Exception?.GetBaseException().Message ?? "The keystore read was cancelled");
		}

		var read = completed.Result;
		try
		{
			return read.Status switch
			{
				KekStoreStatus.Found when ringKekId is not null
					&& string.Equals(KeyRingKeyDerivation.DeriveKekId(read.Kek!), ringKekId, StringComparison.Ordinal)
					=> new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.Recovered, null),
				KekStoreStatus.Found => new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.EntryStale, null),
				KekStoreStatus.NotFound
					=> new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.EntryMissing, null),
				_ => new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.Unavailable, read.Reason)
			};
		}
		finally
		{
			if (read.Kek is not null)
			{
				CryptographicOperations.ZeroMemory(read.Kek);
			}
		}
	}
}
