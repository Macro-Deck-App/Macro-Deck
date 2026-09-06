using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/icon-packs")]
public class IconPacksController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetIconPacksRequest, GetIconPacksResponse> _getIconPacks;
	private readonly IUiTransportMessageHandler<CreateIconPackRequest, CreateIconPackResponse> _createIconPack;
	private readonly IUiTransportMessageHandler<UpdateIconPackRequest, UpdateIconPackResponse> _updateIconPack;
	private readonly IUiTransportMessageHandler<DeleteIconPackRequest, DeleteIconPackResponse> _deleteIconPack;
	private readonly IUiTransportMessageHandler<ImportIconsFromPathRequest, ImportIconsResponse> _importFromPath;
	private readonly IIconImportService _iconImportService;
	private readonly IIconPackExportService _iconPackExportService;
	private readonly IIconPackRestoreService _iconPackRestoreService;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public IconPacksController(
		IUiTransportMessageHandler<GetIconPacksRequest, GetIconPacksResponse> getIconPacks,
		IUiTransportMessageHandler<CreateIconPackRequest, CreateIconPackResponse> createIconPack,
		IUiTransportMessageHandler<UpdateIconPackRequest, UpdateIconPackResponse> updateIconPack,
		IUiTransportMessageHandler<DeleteIconPackRequest, DeleteIconPackResponse> deleteIconPack,
		IUiTransportMessageHandler<ImportIconsFromPathRequest, ImportIconsResponse> importFromPath,
		IIconImportService iconImportService,
		IIconPackExportService iconPackExportService,
		IIconPackRestoreService iconPackRestoreService,
		IconImportBatchTracker batchTracker,
		IIconPackCache iconPackCache,
		IIconPackOwnerRegistry ownerRegistry)
	{
		_getIconPacks = getIconPacks;
		_createIconPack = createIconPack;
		_updateIconPack = updateIconPack;
		_deleteIconPack = deleteIconPack;
		_importFromPath = importFromPath;
		_iconImportService = iconImportService;
		_iconPackExportService = iconPackExportService;
		_iconPackRestoreService = iconPackRestoreService;
		_batchTracker = batchTracker;
		_iconPackCache = iconPackCache;
		_ownerRegistry = ownerRegistry;
	}

	[HttpGet]
	public Task<GetIconPacksResponse> GetAll(CancellationToken ct)
		=> _getIconPacks.Handle(new GetIconPacksRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateIconPackResponse> Create(CreateIconPackRequest body, CancellationToken ct)
		=> _createIconPack.Handle(body, ct).AsTask();

	[HttpPut("{id}")]
	public Task<UpdateIconPackResponse> Update(string id, UpdateIconPackRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _updateIconPack.Handle(body, ct).AsTask();
	}

	[HttpDelete("{id}")]
	public Task<DeleteIconPackResponse> Delete(string id, CancellationToken ct)
		=> _deleteIconPack.Handle(new DeleteIconPackRequest { Id = id }, ct).AsTask();

	[HttpPost("import")]
	[DisableFormValueModelBinding]
	[RequestSizeLimit(IconUploadReader.MaxRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = IconUploadReader.MaxRequestBytes)]
	public async Task<ImportIconPacksResponse> ImportPacks(CancellationToken ct)
	{
		var files = IconUploadReader.TryReadFiles(Request, ct);
		if (files is null)
		{
			return new ImportIconPacksResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.ValidationError),
					Message = AppStrings.Errors.Common.MultipartBodyRequired()
				}
			};
		}

		var result = await _iconImportService.ImportPacks(files, ct);
		return IconMapper.ToImportPacksResponse(result, _batchTracker, _iconPackCache, _ownerRegistry);
	}

	[HttpPost("{id}/import")]
	[DisableFormValueModelBinding]
	[RequestSizeLimit(IconUploadReader.MaxRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = IconUploadReader.MaxRequestBytes)]
	public async Task<ImportIconsResponse> Import(string id, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var packId))
		{
			return InvalidRequest(AppStrings.Errors.Icons.PackIdRequired());
		}

		var files = IconUploadReader.TryReadFiles(Request, ct);
		if (files is null)
		{
			return InvalidRequest(AppStrings.Errors.Common.MultipartBodyRequired());
		}

		var result = await _iconImportService.Import(packId, sourceName: null, files, ct);
		return IconMapper.ToImportResponse(result, _batchTracker);
	}

	[HttpGet("{id}/export")]
	public async Task<IActionResult> Export(string id, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var packId))
		{
			return NotFound();
		}

		var fileName = _iconPackExportService.GetExportFileName(packId);
		if (!fileName.Success)
		{
			return NotFound();
		}

		var disposition = new ContentDispositionHeaderValue("attachment");
		disposition.SetHttpFileName(fileName.Data);
		Response.ContentType = "application/zip";
		Response.Headers.ContentDisposition = disposition.ToString();

		var bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();
		if (bodyControl is not null)
		{
			bodyControl.AllowSynchronousIO = true;
		}

		var result = await _iconPackExportService.Export(packId, Response.Body, ct);
		if (!result.Success && !Response.HasStarted)
		{
			return NotFound();
		}

		return new EmptyResult();
	}

	[HttpPost("{id}/import-path")]
	public Task<ImportIconsResponse> ImportPath(string id, ImportIconsFromPathRequest body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return Task.FromResult(new ImportIconsResponse { Success = false, Error = refusal });
		}

		body.PackId = id;
		return _importFromPath.Handle(body, ct).AsTask();
	}

	[HttpPost("restore-from-path")]
	public async Task<ImportIconPacksResponse> RestoreFromPath(RestoreIconPackFromPathRequest body,
		CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new ImportIconPacksResponse { Success = false, Error = refusal };
		}

		var archive = ArchivePathReader.OpenRead(body.Path,
			[IconImportFiles.MacroDeckIconPackExtension],
			IconUploadReader.MaxRequestBytes);
		if (!archive.Success)
		{
			return new ImportIconPacksResponse
			{
				Success = false,
				Error = PortabilityHttp.ToTransportError(archive.Error!.Value, archive.ErrorMessage)
			};
		}

		await using var content = archive.Data!;
		var result = await _iconPackRestoreService.RestoreAsNewPack(Path.GetFileName(body.Path), content, ct);
		if (!result.Success)
		{
			return IconMapper.ToImportPacksResponse(
				Result.Fail<IconPackImportResult, IconError>(result.Error!.Value, result.ErrorMessage),
				_batchTracker,
				_iconPackCache,
				_ownerRegistry);
		}

		return IconMapper.ToImportPacksResponse(
			Result.Ok<IconPackImportResult, IconError>(new IconPackImportResult(null, [result.Data!])),
			_batchTracker,
			_iconPackCache,
			_ownerRegistry);
	}

	private static ImportIconsResponse InvalidRequest(MacroDeck.Localization.LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError
			{
				Code = nameof(IconError.ValidationError),
				Message = message
			}
		};
}
