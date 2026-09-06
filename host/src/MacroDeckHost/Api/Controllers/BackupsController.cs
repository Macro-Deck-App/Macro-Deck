using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Auth;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using AppBackups = MacroDeckHost.Application.Backups;
using AppBackupsStorage = MacroDeckHost.Application.Backups.Storage;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/backups")]
public class BackupsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetBackupsRequest, GetBackupsResponse> _getBackups;
	private readonly IUiTransportMessageHandler<CreateBackupRequest, CreateBackupResponse> _createBackup;
	private readonly IUiTransportMessageHandler<DeleteBackupRequest, DeleteBackupResponse> _deleteBackup;
	private readonly IUiTransportMessageHandler<GetBackupSettingsRequest, GetBackupSettingsResponse> _getSettings;

	private readonly IUiTransportMessageHandler<UpdateBackupSettingsRequest, UpdateBackupSettingsResponse>
		_updateSettings;

	private readonly IUiTransportMessageHandler<GetBackupRecoveryKeyStateRequest, GetBackupRecoveryKeyStateResponse>
		_getRecoveryKeyState;

	private readonly IUiTransportMessageHandler<CreateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
		_createRecoveryKey;

	private readonly IUiTransportMessageHandler<AcknowledgeBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
		_acknowledgeRecoveryKey;

	private readonly IUiTransportMessageHandler<RevealBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
		_revealRecoveryKey;

	private readonly IUiTransportMessageHandler<RegenerateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
		_regenerateRecoveryKey;

	private readonly IUiTransportMessageHandler<GetBackupStatusRequest, GetBackupStatusResponse> _getStatus;
	private readonly IUiTransportMessageHandler<InspectBackupRequest, InspectBackupResponse> _inspectBackup;
	private readonly IUiTransportMessageHandler<PrepareRestoreRequest, PrepareRestoreResponse> _prepareRestore;
	private readonly IUiTransportMessageHandler<CommitRestoreRequest, CommitRestoreResponse> _commitRestore;
	private readonly IUiTransportMessageHandler<CancelRestoreRequest, CancelRestoreResponse> _cancelRestore;

	private readonly
		IUiTransportMessageHandler<TriggerBeforeHostUpdateBackupRequest, TriggerBeforeHostUpdateBackupResponse>
		_triggerBeforeHostUpdate;

	private readonly AppBackups.IBackupService _backupService;
	private readonly AppBackupsStorage.IBackupStorageRegistry _storageRegistry;
	private readonly IMacroDeckPaths _paths;

	public BackupsController(
		IUiTransportMessageHandler<GetBackupsRequest, GetBackupsResponse> getBackups,
		IUiTransportMessageHandler<CreateBackupRequest, CreateBackupResponse> createBackup,
		IUiTransportMessageHandler<DeleteBackupRequest, DeleteBackupResponse> deleteBackup,
		IUiTransportMessageHandler<GetBackupSettingsRequest, GetBackupSettingsResponse> getSettings,
		IUiTransportMessageHandler<UpdateBackupSettingsRequest, UpdateBackupSettingsResponse> updateSettings,
		IUiTransportMessageHandler<GetBackupRecoveryKeyStateRequest, GetBackupRecoveryKeyStateResponse>
			getRecoveryKeyState,
		IUiTransportMessageHandler<CreateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse> createRecoveryKey,
		IUiTransportMessageHandler<AcknowledgeBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
			acknowledgeRecoveryKey,
		IUiTransportMessageHandler<RevealBackupRecoveryKeyRequest, BackupRecoveryKeyResponse> revealRecoveryKey,
		IUiTransportMessageHandler<RegenerateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse> regenerateRecoveryKey,
		IUiTransportMessageHandler<GetBackupStatusRequest, GetBackupStatusResponse> getStatus,
		IUiTransportMessageHandler<InspectBackupRequest, InspectBackupResponse> inspectBackup,
		IUiTransportMessageHandler<PrepareRestoreRequest, PrepareRestoreResponse> prepareRestore,
		IUiTransportMessageHandler<CommitRestoreRequest, CommitRestoreResponse> commitRestore,
		IUiTransportMessageHandler<CancelRestoreRequest, CancelRestoreResponse> cancelRestore,
		IUiTransportMessageHandler<TriggerBeforeHostUpdateBackupRequest, TriggerBeforeHostUpdateBackupResponse>
			triggerBeforeHostUpdate,
		AppBackups.IBackupService backupService,
		AppBackupsStorage.IBackupStorageRegistry storageRegistry,
		IMacroDeckPaths paths)
	{
		_getBackups = getBackups;
		_createBackup = createBackup;
		_deleteBackup = deleteBackup;
		_getSettings = getSettings;
		_updateSettings = updateSettings;
		_getRecoveryKeyState = getRecoveryKeyState;
		_createRecoveryKey = createRecoveryKey;
		_acknowledgeRecoveryKey = acknowledgeRecoveryKey;
		_revealRecoveryKey = revealRecoveryKey;
		_regenerateRecoveryKey = regenerateRecoveryKey;
		_getStatus = getStatus;
		_inspectBackup = inspectBackup;
		_prepareRestore = prepareRestore;
		_commitRestore = commitRestore;
		_cancelRestore = cancelRestore;
		_triggerBeforeHostUpdate = triggerBeforeHostUpdate;
		_backupService = backupService;
		_storageRegistry = storageRegistry;
		_paths = paths;
	}

	[HttpGet]
	public Task<GetBackupsResponse> GetAll(CancellationToken ct)
		=> _getBackups.Handle(new GetBackupsRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateBackupResponse> Create(CreateBackupRequest body, CancellationToken ct)
		=> _createBackup.Handle(body, ct).AsTask();

	[HttpDelete("{backupId:guid}")]
	public Task<DeleteBackupResponse> Delete(Guid backupId, CancellationToken ct)
		=> _deleteBackup.Handle(new DeleteBackupRequest { BackupId = backupId }, ct).AsTask();

	[HttpGet("settings")]
	public Task<GetBackupSettingsResponse> GetSettings(CancellationToken ct)
		=> _getSettings.Handle(new GetBackupSettingsRequest(), ct).AsTask();

	[HttpPatch("settings")]
	public Task<UpdateBackupSettingsResponse> UpdateSettings(UpdateBackupSettingsRequest body, CancellationToken ct)
		=> _updateSettings.Handle(body, ct).AsTask();

	[HttpGet("recovery-key")]
	public Task<GetBackupRecoveryKeyStateResponse> GetRecoveryKeyState(CancellationToken ct)
		=> _getRecoveryKeyState.Handle(new GetBackupRecoveryKeyStateRequest(), ct).AsTask();

	[HttpPost("recovery-key")]
	public Task<BackupRecoveryKeyResponse> CreateRecoveryKey(CancellationToken ct)
		=> _createRecoveryKey.Handle(new CreateBackupRecoveryKeyRequest(), ct).AsTask();

	[HttpPost("recovery-key/acknowledge")]
	public Task<BackupRecoveryKeyResponse> AcknowledgeRecoveryKey(CancellationToken ct)
		=> _acknowledgeRecoveryKey.Handle(new AcknowledgeBackupRecoveryKeyRequest(), ct).AsTask();

	[HttpGet("status")]
	public Task<GetBackupStatusResponse> GetStatus(CancellationToken ct)
		=> _getStatus.Handle(new GetBackupStatusRequest(), ct).AsTask();

	[HttpGet("{backupId:guid}/download")]
	public async Task<IActionResult> Download(Guid backupId, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		// A backup archive contains the Data Protection key ring, the auth signing key, the TLS private
		// key and every stored secret it protects, so engineering/api/authentication.md forbids returning
		// it - or any other reusable plaintext secret - over the public listener; only a trusted loopback
		// caller may download the raw archive, and it is streamed rather than buffered given its size.
		var result = await _backupService.OpenExport(backupId, ct);
		if (!result.Success)
		{
			return NotFound();
		}

		var handle = result.Data!;
		return File(handle.Content, "application/octet-stream", handle.FileName);
	}

	[HttpPost("{backupId:guid}/inspect")]
	public async Task<IActionResult> Inspect(Guid backupId, InspectBackupRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		body.BackupId = backupId;
		return Ok(await _inspectBackup.Handle(body, ct));
	}

	[HttpPost("inspect")]
	[RequestSizeLimit(BackupHttp.MaxUploadBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = BackupHttp.MaxUploadBytes)]
	public async Task<IActionResult> InspectUpload([FromForm] IFormFile? file,
		[FromForm] string? recoveryKey,
		CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		var staged = await BackupHttp.StageUpload(file, _paths, ct);
		if (!staged.Success)
		{
			return Ok(new InspectBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(staged.Error!.Value, staged.ErrorMessage)
			});
		}

		try
		{
			var source = new AppBackups.BackupSourceRef(null, null, staged.Data);
			var result = await _backupService.Inspect(source, recoveryKey, ct);
			if (!result.Success)
			{
				return Ok(new InspectBackupResponse
				{
					Success = false,
					Error = BackupHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
				});
			}

			return Ok(BackupDtoMapper.ToInspectResponse(result.Data!, string.Empty));
		}
		finally
		{
			BackupHttp.DeleteStaged(staged.Data);
		}
	}

	[HttpPost("inspect-path")]
	public async Task<IActionResult> InspectPath(InspectBackupPathRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		var validation = BackupHttp.ValidatePath(body.Path);
		if (!validation.Success)
		{
			return Ok(new InspectBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(validation.Error!.Value, validation.ErrorMessage)
			});
		}

		var source = new AppBackups.BackupSourceRef(null, null, body.Path);
		var result = await _backupService.Inspect(source, body.RecoveryKey, ct);
		if (!result.Success)
		{
			return Ok(new InspectBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			});
		}

		return Ok(BackupDtoMapper.ToInspectResponse(result.Data!, string.Empty));
	}

	[HttpPost("import")]
	[RequestSizeLimit(BackupHttp.MaxUploadBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = BackupHttp.MaxUploadBytes)]
	public async Task<IActionResult> ImportUpload([FromForm] IFormFile? file, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		var staged = await BackupHttp.StageUpload(file, _paths, ct);
		if (!staged.Success)
		{
			return Ok(new ImportBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(staged.Error!.Value, staged.ErrorMessage)
			});
		}

		try
		{
			var source = new AppBackups.BackupSourceRef(null, null, staged.Data);
			var result = await _backupService.Import(source, ct);
			return Ok(ToImportResponse(result));
		}
		finally
		{
			BackupHttp.DeleteStaged(staged.Data);
		}
	}

	[HttpPost("import-path")]
	public async Task<IActionResult> ImportPath(ImportBackupPathRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		var validation = BackupHttp.ValidatePath(body.Path);
		if (!validation.Success)
		{
			return Ok(new ImportBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(validation.Error!.Value, validation.ErrorMessage)
			});
		}

		var source = new AppBackups.BackupSourceRef(null, null, body.Path);
		var result = await _backupService.Import(source, ct);
		return Ok(ToImportResponse(result));
	}

	[HttpPost("restore/prepare")]
	public async Task<IActionResult> PrepareRestore(PrepareRestoreRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _prepareRestore.Handle(body, ct));
	}

	[HttpPost("restore/commit")]
	public async Task<IActionResult> CommitRestore(CommitRestoreRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _commitRestore.Handle(body, ct));
	}

	[HttpPost("restore/cancel")]
	public async Task<IActionResult> CancelRestore(CancelRestoreRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _cancelRestore.Handle(body, ct));
	}

	[HttpPost("recovery-key/reveal")]
	public async Task<IActionResult> RevealRecoveryKey(RevealBackupRecoveryKeyRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _revealRecoveryKey.Handle(body, ct));
	}

	[HttpPost("recovery-key/regenerate")]
	public async Task<IActionResult> RegenerateRecoveryKey(RegenerateBackupRecoveryKeyRequest body,
		CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _regenerateRecoveryKey.Handle(body, ct));
	}

	[HttpPost("before-host-update")]
	public async Task<IActionResult> TriggerBeforeHostUpdate(TriggerBeforeHostUpdateBackupRequest body,
		CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _triggerBeforeHostUpdate.Handle(body, ct));
	}

	private ImportBackupResponse ToImportResponse(Result<AppBackups.BackupDescriptor, BackupError> result)
	{
		if (!result.Success)
		{
			return new ImportBackupResponse
			{
				Success = false,
				Error = BackupHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var descriptor = result.Data!;
		var displayName = _storageRegistry.Find(descriptor.ProviderId)?.DisplayName ?? descriptor.ProviderId;

		return new ImportBackupResponse { Success = true, Backup = BackupDtoMapper.ToSummary(descriptor, displayName) };
	}
}
