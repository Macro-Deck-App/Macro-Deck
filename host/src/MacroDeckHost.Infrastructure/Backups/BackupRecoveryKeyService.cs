using System.Globalization;
using System.Security.Cryptography;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupRecoveryKeyService : IBackupRecoveryKeyService, IDisposable
{
	private readonly IAppPreferenceRepository _preferences;
	private readonly ISecretService _secrets;
	private readonly IKeyRingProtectionService _keyRing;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _creationGate = new(1, 1);

	public BackupRecoveryKeyService(IAppPreferenceRepository preferences,
		ISecretService secrets,
		IKeyRingProtectionService keyRing,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_preferences = preferences;
		_secrets = secrets;
		_keyRing = keyRing;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<BackupRecoveryKeyService>();
	}

	public async Task<BackupRecoveryKeyState> GetState(CancellationToken cancellationToken = default)
	{
		var secretIdPreference = await _preferences.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);
		if (secretIdPreference is null)
		{
			return new BackupRecoveryKeyState(BackupRecoveryKeyAvailability.None, null, null, null);
		}

		var createdAt = await ReadTimestamp(AppPreferenceService.BackupRecoveryKeyCreatedAtKey);
		var exportedAt = await ReadTimestamp(AppPreferenceService.BackupRecoveryKeyExportedAtKey);

		var key = Guid.TryParse(secretIdPreference.Value, out var secretId) ? await TryResolveKey(secretId) : null;
		if (key is null)
		{
			return new BackupRecoveryKeyState(BackupRecoveryKeyAvailability.Missing, null, createdAt, exportedAt);
		}

		var keyId = BackupKeyDerivation.DeriveKeyId(key);
		CryptographicOperations.ZeroMemory(key);

		return new BackupRecoveryKeyState(BackupRecoveryKeyAvailability.Available, keyId, createdAt, exportedAt);
	}

	public async Task<Result<byte[], BackupError>> EnsureCreated(CancellationToken cancellationToken = default)
	{
		await _creationGate.WaitAsync(cancellationToken);
		try
		{
			var preference = await _preferences.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);

			// While the key ring is locked every stored secret reads as missing, so minting a
			// replacement here would overwrite the one recovery key that still opens the escrow.
			if (_keyRing.Status.State == KeyRingProtectionState.Locked && preference is not null)
			{
				return Result.Fail<byte[], BackupError>(BackupError.RecoveryKeyMissing);
			}

			if (preference is not null && Guid.TryParse(preference.Value, out var existingSecretId))
			{
				var existingKey = await TryResolveKey(existingSecretId);
				if (existingKey is not null)
				{
					return Result.Ok<byte[], BackupError>(existingKey);
				}
			}

			var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
			await StoreNewSecret(FormatKey(key));
			await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeyCreatedAtKey, FormatTimestamp());

			return Result.Ok<byte[], BackupError>(key);
		}
		finally
		{
			_creationGate.Release();
		}
	}

	public async Task<Result<byte[], BackupError>> Resolve(CancellationToken cancellationToken = default)
	{
		var preference = await _preferences.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);
		if (preference is null || !Guid.TryParse(preference.Value, out var secretId))
		{
			return Result.Fail<byte[], BackupError>(BackupError.RecoveryKeyMissing);
		}

		var key = await TryResolveKey(secretId);

		return key is null
			? Result.Fail<byte[], BackupError>(BackupError.RecoveryKeyMissing)
			: Result.Ok<byte[], BackupError>(key);
	}

	public async Task<Result<string, BackupError>> Export(CancellationToken cancellationToken = default)
	{
		var resolved = await Resolve(cancellationToken);
		if (!resolved.Success)
		{
			return Result.Fail<string, BackupError>(resolved.Error!.Value, resolved.ErrorMessage);
		}

		var key = resolved.Data!;
		var formatted = FormatKey(key);
		await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeyExportedAtKey, FormatTimestamp());

		_logger.Information("Backup recovery key {KeyId} was exported", BackupKeyDerivation.DeriveKeyId(key));

		await ProtectKeyRing(key, cancellationToken);

		return Result.Ok<string, BackupError>(formatted);
	}

	public async Task<Result<BackupError>> Acknowledge(CancellationToken cancellationToken = default)
	{
		var preference = await _preferences.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);
		if (preference is null)
		{
			return Result.Fail<BackupError>(BackupError.RecoveryKeyMissing);
		}

		await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeyExportedAtKey, FormatTimestamp());

		var resolved = await Resolve(cancellationToken);
		if (resolved.Success)
		{
			await ProtectKeyRing(resolved.Data!, cancellationToken);
		}

		return Result.Ok<BackupError>();
	}

	public async Task<Result<string, BackupError>> Regenerate(CancellationToken cancellationToken = default)
	{
		await _creationGate.WaitAsync(cancellationToken);
		try
		{
			// Regenerating never unprotects anything, so without this it succeeds while locked and
			// overwrites the recovery key the escrow is waiting for.
			if (_keyRing.Status.State == KeyRingProtectionState.Locked)
			{
				return Result.Fail<string, BackupError>(BackupError.ValidationError,
					"The recovery key cannot be regenerated while the key ring is locked.");
			}

			var key = RandomNumberGenerator.GetBytes(BackupRecoveryKeyFormat.KeyBytes);
			var formatted = FormatKey(key);

			// Escrowed before the stored secret is replaced, and alongside the existing wrap rather
			// than over it: until the replacement has been exported the old key is still the only one
			// the user has written down.
			var escrowed = await _keyRing.AddEscrowWrap(key, cancellationToken);
			if (!escrowed.Success)
			{
				return Result.Fail<string, BackupError>(BackupError.ValidationError,
					"The key ring escrow could not be updated, so the recovery key was left unchanged.");
			}

			var preference = await _preferences.GetByKey(AppPreferenceService.BackupRecoveryKeySecretIdKey);
			var replaced = preference is not null &&
				Guid.TryParse(preference.Value, out var existingSecretId) &&
				await _secrets.Replace(existingSecretId, formatted);

			if (!replaced)
			{
				await StoreNewSecret(formatted);
				await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeyCreatedAtKey, FormatTimestamp());
			}

			await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeyExportedAtKey, string.Empty);

			_logger.Information("Backup recovery key was regenerated, new key id {KeyId}",
				BackupKeyDerivation.DeriveKeyId(key));

			return Result.Ok<string, BackupError>(formatted);
		}
		finally
		{
			_creationGate.Release();
		}
	}

	public bool TryParseExportedKey(string? text, out byte[] key)
		=> BackupRecoveryKeyFormat.TryParseExported(text, out key);

	private async Task ProtectKeyRing(byte[] key, CancellationToken cancellationToken)
	{
		var protection = await _keyRing.EnsureProtected(key, cancellationToken);
		if (protection.Success)
		{
			await _keyRing.PruneEscrowWrapsExcept(key, cancellationToken);

			return;
		}

		// Reported through the key ring status rather than failing the export: the user has their key
		// in hand either way, which is the outcome that matters here.
		_logger.Warning("The key ring could not be protected ({Error})", protection.Error);
	}

	private async Task StoreNewSecret(string formatted)
	{
		var secretId = await _secrets.Create(formatted, SecretKind.Secret);
		await _preferences.SetValue(AppPreferenceService.BackupRecoveryKeySecretIdKey, secretId.ToString("D"));
	}

	private async Task<byte[]?> TryResolveKey(Guid secretId)
	{
		string? formatted;
		try
		{
			formatted = await _secrets.Resolve(secretId);
		}
		catch (CryptographicException)
		{
			// A DataProtection key that was rotated or lost leaves the stored ciphertext unprotectable;
			// callers treat that exactly like a missing key rather than seeing an unexpected exception.
			return null;
		}

		if (formatted is null)
		{
			return null;
		}

		var key = new byte[BackupRecoveryKeyFormat.KeyBytes];
		Span<byte> checksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];

		return BackupRecoveryKeyFormat.TryParse(formatted, key, checksum, out _) ? key : null;
	}

	private async Task<DateTimeOffset?> ReadTimestamp(string key)
	{
		var value = (await _preferences.GetByKey(key))?.Value;

		return DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;
	}

	private string FormatTimestamp()
		=> _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

	private static string FormatKey(ReadOnlySpan<byte> key)
	{
		Span<byte> checksum = stackalloc byte[BackupRecoveryKeyFormat.ChecksumBytes];
		BackupKeyDerivation.DeriveChecksum(key, checksum);

		return BackupRecoveryKeyFormat.Format(key, checksum);
	}

	public void Dispose()
		=> _creationGate.Dispose();
}
