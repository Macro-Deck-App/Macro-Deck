using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Security.KeyRing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Wraps the key ring on an installation that exported its recovery key before this feature existed.
/// Without it the only triggers are exporting, acknowledging or regenerating a recovery key, none of
/// which an established installation ever calls again - so its ring would stay readable indefinitely.
/// </summary>
public sealed class KeyRingProtectionStartupService : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IKeyRingProtectionService _keyRing;
	private readonly ILogger _logger;

	public KeyRingProtectionStartupService(IServiceScopeFactory scopeFactory,
		IKeyRingProtectionService keyRing,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_keyRing = keyRing;
		_logger = logger.ForContext<KeyRingProtectionStartupService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var status = _keyRing.Status;

		// A ring that is protected but still holds a readable file - a key Data Protection rotated in
		// the session that first wrapped it, whose provider was already built without an encryptor -
		// needs finishing rather than a recovery key.
		if (status.State == KeyRingProtectionState.Protected)
		{
			if (status.MigrationPending)
			{
				await _keyRing.RewrapPending(stoppingToken);
			}

			return;
		}

		if (status.State != KeyRingProtectionState.Unprotected ||
			!status.RecoveryKeyExported ||
			status.UnprotectedReason is KeyRingUnprotectedReason.Portable or KeyRingUnprotectedReason.NoKeystore)
		{
			return;
		}

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var recoveryKeys = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();

			var resolved = await recoveryKeys.Resolve(stoppingToken);
			if (!resolved.Success)
			{
				_logger.Warning("Cannot protect the key ring yet: the recovery key could not be resolved");

				return;
			}

			var protection = await _keyRing.EnsureProtected(resolved.Data!, stoppingToken);
			if (!protection.Success)
			{
				_logger.Warning("The key ring could not be protected at startup ({Error})", protection.Error);
			}
		}
		catch (OperationCanceledException)
		{
			// The host is shutting down; the next start tries again.
		}
	}
}
