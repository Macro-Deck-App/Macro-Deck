using MacroDeck.Localization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Security;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetKeyRingStatusRequestMessageHandler
	: IUiTransportMessageHandler<GetKeyRingStatusRequest, GetKeyRingStatusResponse>
{
	private readonly IKeyRingProtectionService _keyRing;
	private readonly IApplicationRestartService _restart;

	public GetKeyRingStatusRequestMessageHandler(IKeyRingProtectionService keyRing,
		IApplicationRestartService restart)
	{
		_keyRing = keyRing;
		_restart = restart;
	}

	public ValueTask<GetKeyRingStatusResponse> Handle(GetKeyRingStatusRequest request,
		CancellationToken cancellationToken)
	{
		var status = _keyRing.Status;
		var restart = _restart.Availability;

		return ValueTask.FromResult(new GetKeyRingStatusResponse
		{
			Locked = status.State == KeyRingProtectionState.Locked,
			LockReason = status.LockReason.ToString(),
			RestartSupported = restart.Supported,
			RestartUnsupportedReason = restart.Reason
		});
	}
}

public sealed class UnlockKeyRingRequestMessageHandler
	: IUiTransportMessageHandler<UnlockKeyRingRequest, UnlockKeyRingResponse>
{
	private readonly IKeyRingProtectionService _keyRing;
	private readonly IApplicationRestartService _restart;

	public UnlockKeyRingRequestMessageHandler(IKeyRingProtectionService keyRing, IApplicationRestartService restart)
	{
		_keyRing = keyRing;
		_restart = restart;
	}

	public async ValueTask<UnlockKeyRingResponse> Handle(UnlockKeyRingRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Parsed through the format helpers rather than IBackupRecoveryKeyService, which reads the
		// database through the very protector that is unavailable while the ring is locked.
		if (!BackupRecoveryKeyFormat.TryParseExported(request.RecoveryKey, out var key))
		{
			return Failure(KeyRingProtectionError.RecoveryKeyInvalid);
		}

		try
		{
			var result = await _keyRing.Unlock(key, cancellationToken);
			if (!result.Success)
			{
				return Failure(result.Error!.Value);
			}
		}
		finally
		{
			System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
		}

		var restart = _restart.Availability;
		var requested = restart.Supported && _restart.Request("key-ring-unlock").Success;

		return new UnlockKeyRingResponse
		{
			Success = true,
			RestartRequested = requested,
			RestartSupported = restart.Supported,
			RestartUnsupportedReason = restart.Reason
		};
	}

	private UnlockKeyRingResponse Failure(KeyRingProtectionError error)
	{
		var restart = _restart.Availability;

		return new UnlockKeyRingResponse
		{
			Success = false,
			Error = KeyRingDtoMapper.ToTransportError(error),
			RestartSupported = restart.Supported,
			RestartUnsupportedReason = restart.Reason
		};
	}
}

public sealed class GetKeyRingProtectionRequestMessageHandler
	: IUiTransportMessageHandler<GetKeyRingProtectionRequest, GetKeyRingProtectionResponse>
{
	private readonly IKeyRingProtectionService _keyRing;

	public GetKeyRingProtectionRequestMessageHandler(IKeyRingProtectionService keyRing) => _keyRing = keyRing;

	public ValueTask<GetKeyRingProtectionResponse> Handle(GetKeyRingProtectionRequest request,
		CancellationToken cancellationToken)
	{
		var status = _keyRing.Status;

		return ValueTask.FromResult(new GetKeyRingProtectionResponse
		{
			State = status.State.ToString(),
			LockReason = status.LockReason.ToString(),
			UnprotectedReason = status.UnprotectedReason.ToString(),
			Backend = status.Backend.ToString(),
			BackendAvailable = status.BackendAvailable,
			BackendUnavailableReason = status.BackendUnavailableReason,
			KekId = status.KekId,
			EscrowWrapCount = status.EscrowWrapCount,
			RecoveryKeyExported = status.RecoveryKeyExported,
			MigrationPending = status.MigrationPending
		});
	}
}

public static class KeyRingDtoMapper
{
	public static TransportError ToTransportError(KeyRingProtectionError error) => new()
	{
		Code = error.ToString(),
		Message = DefaultMessage(error)
	};

	private static LocalizedText DefaultMessage(KeyRingProtectionError error)
		=> error switch
		{
			KeyRingProtectionError.RecoveryKeyInvalid => AppStrings.Errors.KeyRing.RecoveryKeyInvalid(),
			KeyRingProtectionError.KeystoreUnavailable => AppStrings.Errors.KeyRing.KeystoreUnavailable(),
			KeyRingProtectionError.EscrowMissing => AppStrings.Errors.KeyRing.EscrowMissing(),
			KeyRingProtectionError.EscrowUnreadable => AppStrings.Errors.KeyRing.EscrowUnreadable(),
			_ => AppStrings.Errors.KeyRing.Locked()
		};
}
