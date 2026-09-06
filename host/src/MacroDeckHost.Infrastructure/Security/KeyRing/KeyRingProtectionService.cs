using System.Security.Cryptography;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public sealed class KeyRingProtectionService : IKeyRingProtectionService, IDisposable
{
	private readonly IMacroDeckPaths _paths;
	private readonly IKekStore _store;
	private readonly KekStoreIdentity _identity;
	private readonly KekEscrowStore _escrow;
	private readonly KeyRingKekHolder _holder;
	private readonly KeyRingMigrator _migrator;
	private readonly ILogger _logger;
	private readonly bool _portable;

	// The one gate for every escrow and keystore write. BackupRecoveryKeyService cannot serve this
	// purpose: it is registered scoped, so its own semaphore is per-scope and serialises nothing.
	private readonly SemaphoreSlim _gate = new(1, 1);

	private KeyRingProtectionPlan _plan;
	private bool _recoveryKeyExported;

	public KeyRingProtectionService(IMacroDeckPaths paths,
		IKekStore store,
		KekStoreIdentity identity,
		KeyRingKekHolder holder,
		KeyRingProtectionPlan plan,
		bool portable,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_paths = paths;
		_store = store;
		_identity = identity;
		_holder = holder;
		_plan = plan;
		_portable = portable;
		_logger = logger.ForContext<KeyRingProtectionService>();
		_escrow = new KekEscrowStore(logger, timeProvider);
		_migrator = new KeyRingMigrator(logger);
		_recoveryKeyExported = plan.UnprotectedReason != KeyRingUnprotectedReason.RecoveryKeyNotExported;
	}

	public KeyRingProtectionStatus Status
	{
		get
		{
			var document = _plan.Mode == KeyRingProtectionMode.Unprotected
				? null
				: _escrow.Read(_paths.KeysDirectory);

			// Probed rather than taken from the startup plan: a keychain can lock, or a keyring daemon
			// stop, long after the host started, and the settings screen should say what is true now.
			var availability = _store.Availability;

			return new KeyRingProtectionStatus(ToState(_plan.Mode),
				_plan.LockReason,
				_plan.UnprotectedReason,
				_plan.Backend,
				availability.Supported,
				availability.Reason,
				_plan.Mode == KeyRingProtectionMode.Protected ? _holder.KekId : _plan.RingKekId,
				document?.Wraps.Count ?? 0,
				_recoveryKeyExported,
				_plan.MigrationPending);
		}
	}

	public async Task<Result<KeyRingProtectionError>> EnsureProtected(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_recoveryKeyExported = true;

			if (_plan.Mode == KeyRingProtectionMode.Locked)
			{
				return Result.Fail(KeyRingProtectionError.Locked);
			}

			if (_portable)
			{
				// Binding a portable data root to one machine's keystore would defeat the only thing
				// portable mode exists for.
				_logger.Information("Leaving the key ring unprotected: this is a portable installation");

				return Result.Ok<KeyRingProtectionError>();
			}

			if (!_store.Availability.Supported)
			{
				_logger.Warning("Leaving the key ring unprotected: {Reason}", _store.Availability.Reason);

				_plan = _plan with { UnprotectedReason = KeyRingUnprotectedReason.NoKeystore };

				return Result.Fail(KeyRingProtectionError.KeystoreUnavailable);
			}

			var kek = ResolveKek(recoveryKey.Span);
			try
			{
				// The escrow is written before anything else. From this instant the key is recoverable
				// from the recovery key alone, whatever fails next.
				_escrow.AddWrap(_paths.KeysDirectory, kek, recoveryKey.Span);

				if (_store.Write(_identity, kek) != KekStoreStatus.Found)
				{
					// Availability only says the backend loaded, not that it can answer - libsecret
					// installed with no session bus reaches exactly here. The escrow written a moment
					// ago protects nothing now, so it does not stay on disk.
					_logger.Error("Could not store the key encryption key; leaving the key ring as it is");
					TryDeleteEscrow();
					_plan = _plan with { UnprotectedReason = KeyRingUnprotectedReason.NoKeystore };

					return Result.Fail(KeyRingProtectionError.KeystoreUnavailable);
				}

				_holder.Set(kek);

				var outcome = _migrator.Migrate(_paths.KeysDirectory,
					new KeyRingXmlEncryptor(_holder),
					new KeyRingXmlDecryptor(_holder));

				_plan = _plan with
				{
					Mode = KeyRingProtectionMode.Protected,
					UnprotectedReason = KeyRingUnprotectedReason.None,
					RingKekId = _holder.KekId,
					// Skipped files are ones the migrator will never convert - foreign, unreadable, or
					// DPAPI on the wrong platform. Reporting those as pending would pin the notice and
					// re-run the sweep on every boot for something that can never finish.
					MigrationPending = false
				};

				return Result.Ok<KeyRingProtectionError>();
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
			{
				_logger.Error(e, "Could not protect the key ring");

				return Result.Fail(KeyRingProtectionError.MigrationFailed);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(kek);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<Result<KeyRingProtectionError>> AddEscrowWrap(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_plan.Mode == KeyRingProtectionMode.Locked)
			{
				return Result.Fail(KeyRingProtectionError.Locked);
			}

			var kek = _holder.TryGetKek();
			if (kek is null)
			{
				// Nothing is wrapped yet, so there is nothing to escrow. Protection happens when the
				// recovery key is next exported.
				return Result.Ok<KeyRingProtectionError>();
			}

			try
			{
				_escrow.AddWrap(_paths.KeysDirectory, kek, recoveryKey.Span);

				return Result.Ok<KeyRingProtectionError>();
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				_logger.Error(e, "Could not add a key ring escrow wrap");

				return Result.Fail(KeyRingProtectionError.EscrowUnreadable);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(kek);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<Result<KeyRingProtectionError>> PruneEscrowWrapsExcept(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_plan.Mode != KeyRingProtectionMode.Protected)
			{
				return Result.Ok<KeyRingProtectionError>();
			}

			var result = _escrow.PruneExcept(_paths.KeysDirectory, BackupKeyDerivation.DeriveKeyId(recoveryKey.Span));
			if (result == KekEscrowResult.WouldEmpty)
			{
				_logger.Warning("Refusing to prune the key ring escrow: no wrap matches the current recovery key");
			}

			return Result.Ok<KeyRingProtectionError>();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<Result<KeyRingProtectionError>> Unlock(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_plan.Mode != KeyRingProtectionMode.Locked)
			{
				// Nothing to unlock. Writing anyway would let a stale escrow reached over loopback
				// replace a working entry and lock a host that was fine.
				return Result.Ok<KeyRingProtectionError>();
			}

			var (result, kek) = _escrow.TryOpen(_paths.KeysDirectory, recoveryKey.Span);
			if (result != KekEscrowResult.Ok || kek is null)
			{
				return Result.Fail(result switch
				{
					KekEscrowResult.Missing => KeyRingProtectionError.EscrowMissing,
					KekEscrowResult.Unreadable => KeyRingProtectionError.EscrowUnreadable,
					_ => KeyRingProtectionError.RecoveryKeyInvalid
				});
			}

			try
			{
				// An escrow can name a key that is not the one this ring was wrapped with - a restored
				// escrow beside a local ring, say. Storing it would report success and come straight
				// back locked.
				if (_plan.RingKekId is not null &&
					!string.Equals(KeyRingKeyDerivation.DeriveKekId(kek), _plan.RingKekId, StringComparison.Ordinal))
				{
					return Result.Fail(KeyRingProtectionError.RecoveryKeyInvalid);
				}

				if (_store.Write(_identity, kek) != KekStoreStatus.Found)
				{
					return Result.Fail(KeyRingProtectionError.KeystoreUnavailable);
				}

				_logger.Information("The key ring key encryption key was recovered from the escrow");

				return Result.Ok<KeyRingProtectionError>();
			}
			finally
			{
				CryptographicOperations.ZeroMemory(kek);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<Result<KeyRingProtectionError>> RewrapPending(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_plan.Mode != KeyRingProtectionMode.Protected || !_holder.HasKek)
			{
				return Result.Ok<KeyRingProtectionError>();
			}

			try
			{
				var outcome = _migrator.Migrate(_paths.KeysDirectory,
					new KeyRingXmlEncryptor(_holder),
					new KeyRingXmlDecryptor(_holder));

				_plan = _plan with { MigrationPending = false };
				if (outcome.Skipped > 0)
				{
					_logger.Warning("{Count} key ring file(s) cannot be protected and were left alone",
						outcome.Skipped);
				}

				return Result.Ok<KeyRingProtectionError>();
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
			{
				_logger.Error(e, "Could not wrap the remaining key ring files");

				return Result.Fail(KeyRingProtectionError.MigrationFailed);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public Result<KeyRingProtectionError> AdoptKek(ReadOnlyMemory<byte> kek)
		=> _store.Write(_identity, kek.Span) == KekStoreStatus.Found
			? Result.Ok<KeyRingProtectionError>()
			: Result.Fail(KeyRingProtectionError.KeystoreUnavailable);

	public void Dispose() => _gate.Dispose();

	private void TryDeleteEscrow()
	{
		try
		{
			File.Delete(KekEscrowStore.PathFor(_paths.KeysDirectory));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(e, "Could not remove the key ring escrow that protects nothing");
		}
	}

	/// <summary>
	/// Prefers a key that already exists over minting one. A second key encryption key would leave every
	/// file wrapped by the first permanently unopenable.
	/// </summary>
	private byte[] ResolveKek(ReadOnlySpan<byte> recoveryKey)
	{
		var held = _holder.TryGetKek();
		if (held is not null)
		{
			return held;
		}

		var (result, escrowed) = _escrow.TryOpen(_paths.KeysDirectory, recoveryKey);
		if (result == KekEscrowResult.Ok && escrowed is not null)
		{
			return escrowed;
		}

		var read = _store.Read(_identity);

		return read.Status == KekStoreStatus.Found ? read.Kek! : KeyRingKeyDerivation.CreateKek();
	}

	private static KeyRingProtectionState ToState(KeyRingProtectionMode mode)
		=> mode switch
		{
			KeyRingProtectionMode.Protected => KeyRingProtectionState.Protected,
			KeyRingProtectionMode.Locked => KeyRingProtectionState.Locked,
			_ => KeyRingProtectionState.Unprotected
		};
}
