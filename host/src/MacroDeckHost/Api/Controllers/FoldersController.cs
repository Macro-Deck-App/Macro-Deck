using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Portable;
using MacroDeckHost.Auth;
using MacroDeckHost.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/folders")]
public class FoldersController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetFoldersRequest, GetFoldersResponse> _getFolders;
	private readonly IUiTransportMessageHandler<CreateFolderRequest, CreateFolderResponse> _createFolder;
	private readonly IUiTransportMessageHandler<UpdateFolderRequest, UpdateFolderResponse> _updateFolder;
	private readonly IUiTransportMessageHandler<DeleteFolderRequest, DeleteFolderResponse> _deleteFolder;
	private readonly IUiTransportMessageHandler<DuplicateFolderRequest, DuplicateFolderResponse> _duplicateFolder;
	private readonly IUiTransportMessageHandler<MoveFolderRequest, MoveFolderResponse> _moveFolder;
	private readonly IUiTransportMessageHandler<GetFolderFocusRulesRequest, GetFolderFocusRulesResponse> _getFocusRules;
	private readonly IUiTransportMessageHandler<SetFolderFocusRuleRequest, SetFolderFocusRuleResponse> _setFocusRule;

	private readonly IUiTransportMessageHandler<DeleteFolderFocusRuleRequest, DeleteFolderFocusRuleResponse>
		_deleteFocusRule;

	private readonly IFolderPortabilityService _portability;

	public FoldersController(
		IUiTransportMessageHandler<GetFoldersRequest, GetFoldersResponse> getFolders,
		IUiTransportMessageHandler<CreateFolderRequest, CreateFolderResponse> createFolder,
		IUiTransportMessageHandler<UpdateFolderRequest, UpdateFolderResponse> updateFolder,
		IUiTransportMessageHandler<DeleteFolderRequest, DeleteFolderResponse> deleteFolder,
		IUiTransportMessageHandler<DuplicateFolderRequest, DuplicateFolderResponse> duplicateFolder,
		IUiTransportMessageHandler<MoveFolderRequest, MoveFolderResponse> moveFolder,
		IUiTransportMessageHandler<GetFolderFocusRulesRequest, GetFolderFocusRulesResponse> getFocusRules,
		IUiTransportMessageHandler<SetFolderFocusRuleRequest, SetFolderFocusRuleResponse> setFocusRule,
		IUiTransportMessageHandler<DeleteFolderFocusRuleRequest, DeleteFolderFocusRuleResponse> deleteFocusRule,
		IFolderPortabilityService portability)
	{
		_getFolders = getFolders;
		_createFolder = createFolder;
		_updateFolder = updateFolder;
		_deleteFolder = deleteFolder;
		_duplicateFolder = duplicateFolder;
		_moveFolder = moveFolder;
		_getFocusRules = getFocusRules;
		_setFocusRule = setFocusRule;
		_deleteFocusRule = deleteFocusRule;
		_portability = portability;
	}

	[HttpGet]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetFoldersResponse> GetAll([FromQuery] string? profileId, CancellationToken ct)
		=> _getFolders.Handle(new GetFoldersRequest { ProfileId = profileId }, ct).AsTask();

	[HttpPost]
	public Task<CreateFolderResponse> Create(CreateFolderRequest body, CancellationToken ct)
		=> _createFolder.Handle(body, ct).AsTask();

	[HttpPut]
	public Task<UpdateFolderResponse> Update(UpdateFolderRequest body, CancellationToken ct)
		=> _updateFolder.Handle(body, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteFolderResponse> Delete(string id, CancellationToken ct)
		=> _deleteFolder.Handle(new DeleteFolderRequest { Id = id }, ct).AsTask();

	[HttpPost("{id}/duplicate")]
	public Task<DuplicateFolderResponse> Duplicate(string id, CancellationToken ct)
		=> _duplicateFolder.Handle(new DuplicateFolderRequest { Id = id }, ct).AsTask();

	[HttpPost("{id}/move")]
	public Task<MoveFolderResponse> Move(string id, MoveFolderRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _moveFolder.Handle(body, ct).AsTask();
	}

	[HttpGet("focus-rules")]
	public Task<GetFolderFocusRulesResponse> GetFocusRules(CancellationToken ct)
		=> _getFocusRules.Handle(new GetFolderFocusRulesRequest(), ct).AsTask();

	[HttpPut("{id}/focus-rules")]
	public Task<SetFolderFocusRuleResponse> SetFocusRule(string id,
		SetFolderFocusRuleRequest body,
		CancellationToken ct)
	{
		body.FolderId = id;
		return _setFocusRule.Handle(body, ct).AsTask();
	}

	[HttpDelete("{id}/focus-rules/{ruleId}")]
	public Task<DeleteFolderFocusRuleResponse> DeleteFocusRule(string id, string ruleId, CancellationToken ct)
		=> _deleteFocusRule.Handle(new DeleteFolderFocusRuleRequest { FolderId = id, RuleId = ruleId }, ct).AsTask();

	[HttpPost("{id}/export")]
	public async Task<IActionResult> Export(string id, ExportFolderRequest body, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var folderId))
		{
			return NotFound();
		}

		var fileName = _portability.GetExportFileName(folderId);
		if (!fileName.Success)
		{
			return PortabilityHttp.ToErrorResult(this, fileName.Error!.Value, fileName.ErrorMessage);
		}

		var result = await _portability.Export(folderId, body.ToOptions(), ct);
		if (!result.Success)
		{
			return PortabilityHttp.ToErrorResult(this, result.Error!.Value, result.ErrorMessage);
		}

		return File(result.Data!, "application/zip", fileName.Data);
	}

	[HttpPost("import")]
	[RequestSizeLimit(PortabilityHttp.MaxRequestBytes)]
	public async Task<ImportFolderResponse> Import([FromForm] IFormFile? file,
		[FromForm] string profileId,
		[FromForm] string? parentFolderId,
		[FromForm] string? password,
		CancellationToken ct)
	{
		var target = ResolveTarget(profileId, parentFolderId);
		if (!target.Success)
		{
			return Fail(target.Error!.Value, target.ErrorMessage);
		}

		var bytes = await ArchivePathReader.ReadUpload(file, ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(target.Data, bytes.Data!, password, ct);
	}

	[HttpPost("import-path")]
	public async Task<ImportFolderResponse> ImportFromPath(ImportFolderFromPathRequest body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new ImportFolderResponse { Success = false, Error = refusal };
		}

		var target = ResolveTarget(body.ProfileId, body.ParentFolderId);
		if (!target.Success)
		{
			return Fail(target.Error!.Value, target.ErrorMessage);
		}

		var bytes = await ArchivePathReader.Read(body.Path,
			[PortableFileExtensions.Folder],
			PortabilityHttp.MaxRequestBytes,
			ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(target.Data, bytes.Data!, body.Password, ct);
	}

	private async Task<ImportFolderResponse> Import(FolderImportTarget target,
		byte[] archiveBytes,
		string? password,
		CancellationToken ct)
	{
		var result = await _portability.Import(target.ProfileId, target.ParentFolderId, archiveBytes, password, ct);
		if (!result.Success)
		{
			return Fail(result.Error!.Value, result.ErrorMessage);
		}

		return new ImportFolderResponse
		{
			Success = true,
			Folder = FolderDtoMapper.MapToDto(result.Data![0]),
			FolderCount = result.Data!.Count
		};
	}

	private static Result<FolderImportTarget, Domain.Enums.PortabilityError> ResolveTarget(string profileId,
		string? parentFolderId)
	{
		if (!Guid.TryParse(profileId, out var targetProfileId))
		{
			return Result.Fail<FolderImportTarget, Domain.Enums.PortabilityError>(
				Domain.Enums.PortabilityError.ValidationError,
				"A valid profile id is required");
		}

		Guid? targetParentId = null;
		if (!string.IsNullOrEmpty(parentFolderId))
		{
			if (!Guid.TryParse(parentFolderId, out var parsedParentId))
			{
				return Result.Fail<FolderImportTarget, Domain.Enums.PortabilityError>(
					Domain.Enums.PortabilityError.ValidationError,
					"A valid parent folder id is required");
			}

			targetParentId = parsedParentId;
		}

		return Result.Ok<FolderImportTarget, Domain.Enums.PortabilityError>(
			new FolderImportTarget(targetProfileId, targetParentId));
	}

	private readonly record struct FolderImportTarget(Guid ProfileId, Guid? ParentFolderId);

	private static ImportFolderResponse Fail(Domain.Enums.PortabilityError error, string? message)
		=> new()
		{
			Success = false,
			Error = PortabilityHttp.ToTransportError(error, message)
		};
}
