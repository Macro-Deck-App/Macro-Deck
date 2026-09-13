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
	private const string RenewedDedupeKey = "host-identity-renewed";

	private readonly Lock _gate = new();
	private readonly IDataProtector _protector;
	private readonly string _keyFilePath;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUserNotificationStore _notifications;
	private readonly ILocalizationResolver _localization;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	// Deliberately not a Lazy: a failed load must be retried on the next call rather than cached until restart.
	private ECDsa? _key;
	private byte[]? _publicKey;

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

	public byte[] PublicKey
	{
		get
		{
			lock (_gate)
			{
				EnsureLoaded();
				return _publicKey!;
			}
		}
	}

	public byte[] Sign(ReadOnlySpan<byte> message)
	{
		lock (_gate)
		{
			EnsureLoaded();
			return _key!.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
		}
	}

	public void Dispose() => _key?.Dispose();

	private void EnsureLoaded()
	{
		if (_key is not null)
		{
			return;
		}

		try
		{
			_key = LoadOrCreate();
		}
		catch (Exception e) when (e is not HostIdentityUnavailableException)
		{
			_logger.Error(e, "The host identity key could not be loaded; it is retried on the next request");
			throw new HostIdentityUnavailableException("The host identity key could not be loaded.", e);
		}

		var point = _key.ExportParameters(includePrivateParameters: false).Q;
		_publicKey = [0x04, .. point.X!, .. point.Y!];
	}

	private ECDsa LoadOrCreate()
	{
		if (!File.Exists(_keyFilePath))
		{
			return CreateForMissingFile();
		}

		var protectedBytes = File.ReadAllBytes(_keyFilePath);
		try
		{
			var key = ECDsa.Create();
			key.ImportPkcs8PrivateKey(_protector.Unprotect(protectedBytes), out _);
			return key;
		}
		catch (CryptographicException e)
		{
			return Renew(e);
		}
	}

	private ECDsa CreateForMissingFile()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var issuedBefore = preferences.GetByKey(AppPreferenceService.HostIdentityIssuedKey).GetAwaiter().GetResult()
			is not null;

		var key = CreateAndStore();

		if (issuedBefore)
		{
			// The first key after an upgrade stays silent: devices paired before it are unpinned and pin on
			// their next exchange. A key missing after one was issued breaks every pin.
			_logger.Warning("The host identity key was missing and has been recreated; paired devices must scan the QR code again");
			RaiseRenewedNotification(scope.ServiceProvider);
		}
		else
		{
			preferences.SetValue(AppPreferenceService.HostIdentityIssuedKey, "true").GetAwaiter().GetResult();
		}

		return key;
	}

	private ECDsa Renew(CryptographicException cause)
	{
		var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
		var aside = $"{_keyFilePath}.unreadable-{stamp}";
		File.Move(_keyFilePath, aside, overwrite: true);
		_logger.Warning(cause,
			"The host identity key could not be unprotected and has been renewed; the old file is kept as {Path}",
			aside);

		var key = CreateAndStore();

		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		preferences.SetValue(AppPreferenceService.HostIdentityIssuedKey, "true").GetAwaiter().GetResult();
		RaiseRenewedNotification(scope.ServiceProvider);

		return key;
	}

	private ECDsa CreateAndStore()
	{
		var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
		var tempPath = $"{_keyFilePath}.tmp";
		File.WriteAllBytes(tempPath, _protector.Protect(key.ExportPkcs8PrivateKey()));
		File.Move(tempPath, _keyFilePath, overwrite: true);

		return key;
	}

	private void RaiseRenewedNotification(IServiceProvider services)
	{
		var culture = services.GetRequiredService<IAppPreferenceService>().GetLocalization().GetAwaiter().GetResult()
			.Culture;
		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Security,
			Title = _localization.Resolve(AppStrings.Notifications.HostIdentityRenewed(), culture),
			Message = _localization.Resolve(AppStrings.Notifications.HostIdentityRenewedMessage(), culture),
			DedupeKey = RenewedDedupeKey
		});
	}
}

// Registered while the key ring is locked, so the real key file is never touched on the way past the lock.
public sealed class LockedHostIdentityKeyProvider : IHostIdentityKeyProvider
{
	public byte[] PublicKey => throw Locked();

	public byte[] Sign(ReadOnlySpan<byte> message) => throw Locked();

	private static HostIdentityUnavailableException Locked() => new("The key ring is locked.");
}
