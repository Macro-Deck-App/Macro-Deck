using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/icons")]
public class IconsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetIconsRequest, GetIconsResponse> _getIcons;
	private readonly IUiTransportMessageHandler<UpdateIconRequest, UpdateIconResponse> _updateIcon;
	private readonly IUiTransportMessageHandler<DeleteIconRequest, DeleteIconResponse> _deleteIcon;
	private readonly IUiTransportMessageHandler<DeleteIconsRequest, DeleteIconsResponse> _deleteIcons;

	private readonly IUiTransportMessageHandler<GetIconImportBatchRequest, GetIconImportBatchResponse>
		_getImportBatch;

	private readonly IUiTransportMessageHandler<CancelIconImportBatchRequest, CancelIconImportBatchResponse>
		_cancelImportBatch;

	private readonly IUiTransportMessageHandler<ImportSingleIconFromPathRequest, ImportSingleIconResponse>
		_importSingleFromPath;

	private readonly IIconImportService _iconImportService;
	private readonly IIconService _iconService;
	private readonly IconImportBatchTracker _batchTracker;

	public IconsController(
		IUiTransportMessageHandler<GetIconsRequest, GetIconsResponse> getIcons,
		IUiTransportMessageHandler<UpdateIconRequest, UpdateIconResponse> updateIcon,
		IUiTransportMessageHandler<DeleteIconRequest, DeleteIconResponse> deleteIcon,
		IUiTransportMessageHandler<DeleteIconsRequest, DeleteIconsResponse> deleteIcons,
		IUiTransportMessageHandler<GetIconImportBatchRequest, GetIconImportBatchResponse> getImportBatch,
		IUiTransportMessageHandler<CancelIconImportBatchRequest, CancelIconImportBatchResponse> cancelImportBatch,
		IUiTransportMessageHandler<ImportSingleIconFromPathRequest, ImportSingleIconResponse> importSingleFromPath,
		IIconImportService iconImportService,
		IIconService iconService,
		IconImportBatchTracker batchTracker)
	{
		_getIcons = getIcons;
		_updateIcon = updateIcon;
		_deleteIcon = deleteIcon;
		_deleteIcons = deleteIcons;
		_getImportBatch = getImportBatch;
		_cancelImportBatch = cancelImportBatch;
		_importSingleFromPath = importSingleFromPath;
		_iconImportService = iconImportService;
		_iconService = iconService;
		_batchTracker = batchTracker;
	}

	[HttpGet]
	public Task<GetIconsResponse> GetAll([FromQuery] string packId, CancellationToken ct)
		=> _getIcons.Handle(new GetIconsRequest { PackId = packId }, ct).AsTask();

	[HttpPatch("{id}")]
	public Task<UpdateIconResponse> Update(string id, UpdateIconRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _updateIcon.Handle(body, ct).AsTask();
	}

	[HttpDelete("{id}")]
	public Task<DeleteIconResponse> Delete(string id, CancellationToken ct)
		=> _deleteIcon.Handle(new DeleteIconRequest { Id = id }, ct).AsTask();

	[HttpPost("delete")]
	public Task<DeleteIconsResponse> DeleteMany(DeleteIconsRequest body, CancellationToken ct)
		=> _deleteIcons.Handle(body, ct).AsTask();

	[HttpPost("import")]
	[DisableFormValueModelBinding]
	[RequestSizeLimit(IconUploadReader.MaxRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = IconUploadReader.MaxRequestBytes)]
	public async Task<ImportIconsResponse> Import(CancellationToken ct)
	{
		var files = IconUploadReader.TryReadFiles(Request, ct);
		if (files is null)
		{
			return new ImportIconsResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.ValidationError),
					Message = AppStrings.Errors.Common.MultipartBodyRequired()
				}
			};
		}

		var result = await _iconImportService.Import(packId: null, sourceName: null, files, ct);
		return IconMapper.ToImportResponse(result, _batchTracker);
	}

	[HttpPost("single-from-path")]
	public Task<ImportSingleIconResponse> ImportSingleFromPath(ImportSingleIconFromPathRequest body,
		CancellationToken ct)
		=> _importSingleFromPath.Handle(body, ct).AsTask();

	[HttpGet("import-batches/{batchId}")]
	public Task<GetIconImportBatchResponse> GetImportBatch(string batchId, CancellationToken ct)
		=> _getImportBatch.Handle(new GetIconImportBatchRequest { BatchId = batchId }, ct).AsTask();

	[HttpPost("import-batches/{batchId}/cancel")]
	public Task<CancelIconImportBatchResponse> CancelImportBatch(string batchId, CancellationToken ct)
		=> _cancelImportBatch.Handle(new CancelIconImportBatchRequest { BatchId = batchId }, ct).AsTask();

	[HttpGet("{id}/image")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<IActionResult> GetImage(string id, [FromQuery] int? size, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var iconId))
		{
			return NotFound();
		}

		var acceptWebp = Request.Headers.Accept.Any(v => v is not null && v.Contains("image/webp"));
		var result = await _iconService.GetImage(iconId, size, acceptWebp, staticFrame: false, ct);
		if (!result.Success)
		{
			Response.Headers.CacheControl = "no-store";
			return NotFound();
		}

		var image = result.Data!;
		if (Request.Headers.IfNoneMatch.Any(v => v is not null && v.Contains(image.ETag)))
		{
			await image.Content.DisposeAsync();
			Response.Headers.ETag = image.ETag;
			return StatusCode(StatusCodes.Status304NotModified);
		}

		Response.Headers.CacheControl = "public, max-age=31536000, immutable";
		Response.Headers.ETag = image.ETag;
		Response.Headers.Vary = "Accept";
		return File(image.Content, image.ContentType);
	}
}
