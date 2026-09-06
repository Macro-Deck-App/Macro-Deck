using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;

public enum KekEscrowResult
{
	Ok,
	Missing,
	Unreadable,
	UnsupportedVersion,
	NoMatchingWrap,
	WouldEmpty
}

public sealed class KekEscrowStore
{
	public const string FileName = "kek.escrow";

	private readonly DurableJsonFile _file;
	private readonly TimeProvider _timeProvider;

	public KekEscrowStore(ILogger logger, TimeProvider timeProvider)
	{
		_file = new DurableJsonFile("key ring escrow",
			PersistenceJsonOptions.Default,
			logger.ForContext<KekEscrowStore>(),
			PersistenceBackup.KeepLastKnownGood);
		_timeProvider = timeProvider;
	}

	public static string PathFor(string keysDirectory) => Path.Combine(keysDirectory, FileName);

	public static bool Exists(string keysDirectory) => File.Exists(PathFor(keysDirectory));

	public KekEscrowDocument? Read(string keysDirectory)
	{
		var document = _file.Read<KekEscrowDocument>(PathFor(keysDirectory));

		// A newer document is refused whole rather than half-understood: silently ignoring wraps a
		// future version added is how a downgrade drops the only key that still opens the escrow.
		return document is null || document.Version != KekEscrowDocument.CurrentVersion ? null : document;
	}

	/// <summary>
	/// Adds or replaces the wrap for one recovery key, leaving every other wrap in place. Creates the
	/// escrow when it does not exist yet.
	/// </summary>
	public KekEscrowResult AddWrap(string keysDirectory, ReadOnlySpan<byte> kek, ReadOnlySpan<byte> recoveryKey)
	{
		var kekId = KeyRingKeyDerivation.DeriveKekId(kek);
		var document = Read(keysDirectory);

		if (document is not null && !string.Equals(document.KekId, kekId, StringComparison.Ordinal))
		{
			// Two different key encryption keys cannot share one escrow: the wraps that named the old
			// one would become unopenable noise, and pruning could not tell them apart.
			document = null;
		}

		document ??= new KekEscrowDocument
		{
			KekId = kekId,
			CreatedAt = _timeProvider.GetUtcNow()
		};

		var recoveryKeyId = BackupKeyDerivation.DeriveKeyId(recoveryKey);
		var salt = RandomNumberGenerator.GetBytes(KeyRingKeyDerivation.SaltBytes);
		var wrappingKey = KeyRingKeyDerivation.DeriveEscrowKey(recoveryKey, salt);
		try
		{
			var sealedValue
				= KeyRingAead.Seal(wrappingKey, kek, AssociatedData(document.Version, kekId, recoveryKeyId));

			document.Wraps.RemoveAll(wrap =>
				string.Equals(wrap.RecoveryKeyId, recoveryKeyId, StringComparison.Ordinal));
			document.Wraps.Add(new KekEscrowWrap
			{
				RecoveryKeyId = recoveryKeyId,
				CreatedAt = _timeProvider.GetUtcNow(),
				Salt = Convert.ToBase64String(salt),
				Nonce = Convert.ToBase64String(sealedValue.Nonce),
				Ciphertext = Convert.ToBase64String(sealedValue.Ciphertext),
				Tag = Convert.ToBase64String(sealedValue.Tag)
			});

			_file.Write(PathFor(keysDirectory), document);

			return KekEscrowResult.Ok;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(wrappingKey);
		}
	}

	/// <summary>
	/// Recovers the key encryption key with a recovery key the user typed. A verifying tag is the only
	/// proof needed that the key is right, which is what makes recovery possible without reading the
	/// database - whose secrets are exactly what is unreadable at that moment.
	/// </summary>
	public (KekEscrowResult Result, byte[]? Kek) TryOpen(string keysDirectory, ReadOnlySpan<byte> recoveryKey)
	{
		if (!Exists(keysDirectory))
		{
			return (KekEscrowResult.Missing, null);
		}

		var document = Read(keysDirectory);
		if (document is null)
		{
			return (KekEscrowResult.Unreadable, null);
		}

		var recoveryKeyId = BackupKeyDerivation.DeriveKeyId(recoveryKey);

		// The named wrap first, then the rest: a wrap whose recorded id is wrong is still worth trying,
		// and the tag decides regardless of what the file claims.
		var ordered = document.Wraps
			.OrderByDescending(wrap => string.Equals(wrap.RecoveryKeyId, recoveryKeyId, StringComparison.Ordinal));

		foreach (var wrap in ordered)
		{
			var kek = TryOpenWrap(document, wrap, recoveryKey);
			if (kek is not null)
			{
				return (KekEscrowResult.Ok, kek);
			}
		}

		return (KekEscrowResult.NoMatchingWrap, null);
	}

	/// <summary>
	/// Drops every wrap except the named one, once its recovery key has demonstrably been exported. It
	/// refuses to leave the escrow empty: an escrow with no wraps recovers nothing, so a prune that
	/// would produce one is a bug, not an outcome.
	/// </summary>
	public KekEscrowResult PruneExcept(string keysDirectory, string recoveryKeyId)
	{
		var document = Read(keysDirectory);
		if (document is null)
		{
			return Exists(keysDirectory) ? KekEscrowResult.Unreadable : KekEscrowResult.Missing;
		}

		if (!document.Wraps.Any(wrap => string.Equals(wrap.RecoveryKeyId, recoveryKeyId, StringComparison.Ordinal)))
		{
			return KekEscrowResult.WouldEmpty;
		}

		if (document.Wraps.Count == 1)
		{
			return KekEscrowResult.Ok;
		}

		document.Wraps.RemoveAll(wrap =>
			!string.Equals(wrap.RecoveryKeyId, recoveryKeyId, StringComparison.Ordinal));
		_file.Write(PathFor(keysDirectory), document);

		return KekEscrowResult.Ok;
	}

	private static byte[]? TryOpenWrap(KekEscrowDocument document, KekEscrowWrap wrap, ReadOnlySpan<byte> recoveryKey)
	{
		byte[] salt;
		KeyRingSealed sealedValue;
		try
		{
			salt = Convert.FromBase64String(wrap.Salt);
			sealedValue = new KeyRingSealed(Convert.FromBase64String(wrap.Nonce),
				Convert.FromBase64String(wrap.Ciphertext),
				Convert.FromBase64String(wrap.Tag));
		}
		catch (FormatException)
		{
			return null;
		}

		var wrappingKey = KeyRingKeyDerivation.DeriveEscrowKey(recoveryKey, salt);
		try
		{
			return KeyRingAead.TryOpen(wrappingKey,
				sealedValue,
				AssociatedData(document.Version, document.KekId, wrap.RecoveryKeyId));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(wrappingKey);
		}
	}

	// Binds a wrap to the key it recovers and the recovery key it claims to be under, so a wrap cannot
	// be moved between escrow files or relabelled to survive a prune.
	private static byte[] AssociatedData(int version, string kekId, string recoveryKeyId)
		=> Encoding.UTF8.GetBytes($"{version}:{kekId}:{recoveryKeyId}");
}
