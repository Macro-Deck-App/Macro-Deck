using System.Globalization;
using System.Security.Cryptography;
using MacroDeck.Localization;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Infrastructure.Auth;

public sealed class FileHostIdentityKeyProvider : IHostIdentityKeyProvider, IDisposable
{
	public const string KeyFileName = "host-identity.key";
	public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
	private const string RenewedDedupeKey = "host-identity-renewed";
	private const string P256Oid = "1.2.840.10045.3.1.7";
	private const int FlagAttempts = 8;
	private static readonly TimeSpan FlagRetryDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

	private readonly SemaphoreSlim _loadGate = new(1, 1);
	private readonly Lock _signGate = new();
	private readonly IDataProtector _protector;
	private readonly string _keyFilePath;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUserNotificationStore _notifications;
	private readonly ILocalizationResolver _localization;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private volatile LoadedKey? _loaded;
	private DateTimeOffset _retryNotBefore = DateTimeOffset.MinValue;
	private bool _failing;
	private readonly CancellationTokenSource _shutdown = new();
	private Task _recording = Task.CompletedTask;

	public FileHostIdentityKeyProvider(IDataProtectionProvider dataProtectionProvider,
		IMacroDeckPaths paths,
		IServiceScopeFactory scopeFactory,
		IUserNotificationStore notifications,
		ILocalizationResolver localization,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_protector = dataProtectionProvider.CreateProtector("MacroDeck.Auth.HostIdentityKey");
		_keyFilePath = Path.Combine(paths.KeysDirectory, KeyFileName);
		_scopeFactory = scopeFactory;
		_notifications = notifications;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async ValueTask<byte[]> GetPublicKey(CancellationToken cancellationToken = default)
		=> (await EnsureLoaded(cancellationToken)).PublicKey;

	public async ValueTask<byte[]> Sign(byte[] message, CancellationToken cancellationToken = default)
	{
		var loaded = await EnsureLoaded(cancellationToken);
		lock (_signGate)
		{
			return loaded.Key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
		}
	}

	public void Dispose()
	{
		// The bookkeeping writes to the database, so a disposed host must not leave it still writing into
		// its data directory.
		_shutdown.Cancel();
		try
		{
			_recording.Wait(ShutdownWait);
		}
		catch (AggregateException e)
		{
			_logger.Warning(e, "The host identity bookkeeping failed while the host stopped");
		}

		_shutdown.Dispose();
		_loaded?.Key.Dispose();
		_loadGate.Dispose();
	}

	private async ValueTask<LoadedKey> EnsureLoaded(CancellationToken cancellationToken)
	{
		if (_loaded is { } loaded)
		{
			return loaded;
		}

		await _loadGate.WaitAsync(cancellationToken);
		try
		{
			if (_loaded is { } current)
			{
				return current;
			}

			var now = _timeProvider.GetUtcNow();
			if (now < _retryNotBefore)
			{
				throw new HostIdentityUnavailableException("The host identity key is unavailable.");
			}

			ECDsa key;
			try
			{
				key = await LoadOrCreate();
			}
			catch (Exception e)
			{
				_retryNotBefore = now + RetryInterval;
				if (!_failing)
				{
					_failing = true;
					_logger.Error(e,
						"The host identity key could not be loaded; retrying at most every {Interval}",
						RetryInterval);
				}

				throw new HostIdentityUnavailableException("The host identity key could not be loaded.", e);
			}

			if (_failing)
			{
				_failing = false;
				_logger.Information("The host identity key is available again");
			}

			var point = key.ExportParameters(includePrivateParameters: false).Q;
			var fresh = new LoadedKey(key, [0x04, .. point.X!, .. point.Y!]);
			_loaded = fresh;
			return fresh;
		}
		finally
		{
			_loadGate.Release();
		}
	}

	private async Task<ECDsa> LoadOrCreate()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);

		// An exclusive handle serialises every provider on this data directory, other processes included,
		// because a no-overwrite move is not atomic on every platform.
		await using var creationLock = new FileStream($"{_keyFilePath}.lock",
			FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.None);

		byte[] protectedBytes;
		try
		{
			protectedBytes = await File.ReadAllBytesAsync(_keyFilePath);
		}
		catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
		{
			return await CreateForMissingFile();
		}

		try
		{
			return Import(protectedBytes);
		}
		catch (CryptographicException e)
		{
			return await Renew(e);
		}
	}

	private async Task<ECDsa> CreateForMissingFile()
	{
		var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		if (!TryStoreNew(key))
		{
			key.Dispose();
			return Import(await File.ReadAllBytesAsync(_keyFilePath));
		}

		RecordCreationInBackground(renewed: false);
		return key;
	}

	private async Task<ECDsa> Renew(CryptographicException cause)
	{
		var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'",
			CultureInfo.InvariantCulture);
		var aside = $"{_keyFilePath}.unreadable-{stamp}";
		File.Move(_keyFilePath, aside, overwrite: true);
		_logger.Warning(cause,
			"The host identity key could not be unprotected and has been renewed; the old file is kept as {Path}",
			aside);

		var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		if (!TryStoreNew(key))
		{
			key.Dispose();
			return Import(await File.ReadAllBytesAsync(_keyFilePath));
		}

		RecordCreationInBackground(renewed: true);
		return key;
	}

	private ECDsa Import(byte[] protectedBytes)
	{
		var key = ECDsa.Create();
		try
		{
			key.ImportPkcs8PrivateKey(_protector.Unprotect(protectedBytes), out _);
			if (key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != P256Oid)
			{
				throw new CryptographicException("The host identity key is not a P-256 key.");
			}

			return key;
		}
		catch
		{
			key.Dispose();
			throw;
		}
	}

	// Never replaces a file: one that appeared since it read as missing is loaded instead.
	private bool TryStoreNew(ECDsa key)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
		var tempPath = $"{_keyFilePath}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(tempPath, _protector.Protect(key.ExportPkcs8PrivateKey()));
		try
		{
			File.Move(tempPath, _keyFilePath, overwrite: false);
			return true;
		}
		catch (IOException) when (File.Exists(_keyFilePath))
		{
			return false;
		}
		finally
		{
			File.Delete(tempPath);
		}
	}

	// The key is usable as soon as its file is written: the flag and the notification are bookkeeping, and a
	// database busy with other startup writers must not make the key unavailable.
	private void RecordCreationInBackground(bool renewed)
	{
		var cancellationToken = _shutdown.Token;
		_recording = Task.Run(() => RecordCreation(renewed, cancellationToken));
	}

	private async Task RecordCreation(bool renewed, CancellationToken cancellationToken)
	{
		var announce = renewed;
		if (!renewed)
		{
			if (await WasIssuedBefore(cancellationToken) is not { } issued)
			{
				return;
			}

			announce = issued;
		}

		if (announce)
		{
			_logger.Warning("A new host identity key was created; paired devices must scan the QR code again");
			await RaiseRenewedNotification(cancellationToken);
		}

		await WithFlagRetry(async preferences =>
				await preferences.SetValue(AppPreferenceService.HostIdentityIssuedKey, "true"),
			"The host identity flag could not be saved; a later missing key would be recreated without a notification",
			cancellationToken);
	}

	// An unreadable flag counts as issued: a needless notification is cheaper than a silent loss of every pin.
	// Null while the host is shutting down.
	private async Task<bool?> WasIssuedBefore(CancellationToken cancellationToken)
	{
		var issued = true;
		var completed = await WithFlagRetry(async preferences =>
				issued = await preferences.GetByKey(AppPreferenceService.HostIdentityIssuedKey) is not null,
			"The host identity flag could not be read; the new key is announced as a renewal",
			cancellationToken);
		return completed ? issued : null;
	}

	// False when the host is shutting down: its services are gone, so retrying would only log noise.
	private async Task<bool> WithFlagRetry(Func<IAppPreferenceRepository, Task> operation, string failureMessage,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1;; attempt++)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				using var scope = _scopeFactory.CreateScope();
				await operation(scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>());
				return true;
			}
			catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
			{
				_logger.Debug("The host is shutting down; the host identity flag is not recorded");
				return false;
			}
			catch (Exception) when (attempt < FlagAttempts)
			{
				await Task.Delay(FlagRetryDelay * attempt, _timeProvider, cancellationToken)
					.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
			catch (Exception e)
			{
				_logger.Warning(e, failureMessage);
				return true;
			}
		}
	}

	private async Task RaiseRenewedNotification(CancellationToken cancellationToken)
	{
		string? culture = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			using var scope = _scopeFactory.CreateScope();
			culture = (await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetLocalization())
				.Culture;
		}
		catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
		{
			_logger.Debug("The host is shutting down; the identity notification uses the default language");
		}
		catch (Exception e)
		{
			_logger.Warning(e, "The interface language could not be read; the identity notification uses the default");
		}

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Security,
			Title = _localization.Resolve(AppStrings.Notifications.HostIdentityRenewed(), culture),
			Message = _localization.Resolve(AppStrings.Notifications.HostIdentityRenewedMessage(), culture),
			DedupeKey = RenewedDedupeKey
		});
	}

	private sealed record LoadedKey(ECDsa Key, byte[] PublicKey);
}

// Registered while the key ring is locked, so the real key file is never touched on the way past the lock.
public sealed class LockedHostIdentityKeyProvider : IHostIdentityKeyProvider
{
	public ValueTask<byte[]> GetPublicKey(CancellationToken cancellationToken = default)
		=> ValueTask.FromException<byte[]>(Locked());

	public ValueTask<byte[]> Sign(byte[] message, CancellationToken cancellationToken = default)
		=> ValueTask.FromException<byte[]>(Locked());

	private static HostIdentityUnavailableException Locked() => new("The key ring is locked.");
}
