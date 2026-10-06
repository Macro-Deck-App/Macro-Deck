using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
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
	private readonly IIconAppearanceService _appearances;
	private readonly IIconPackCache _iconPackCache;

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
		IconImportBatchTracker batchTracker,
		IIconAppearanceService appearances,
		IIconPackCache iconPackCache)
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
		_appearances = appearances;
		_iconPackCache = iconPackCache;
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

	[HttpPost("{id}/appearances")]
	[RequestSizeLimit(IconUploadReader.MaxRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = IconUploadReader.MaxRequestBytes)]
	public async Task<IActionResult> AddAppearance(string id,
		[FromForm] string? key,
		IFormFile? file,
		CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var iconId))
		{
			return AppearanceResult(Result.Fail<IconEntity, IconError>(IconError.NotFound));
		}

		if (file is null || string.IsNullOrEmpty(key))
		{
			return AppearanceResult(Result.Fail<IconEntity, IconError>(IconError.ValidationError,
				"A key and a file are required"));
		}

		await using var content = file.OpenReadStream();
		var result = await _appearances.AddOrReplace(iconId, key, new IconImportFile(file.FileName, content), ct);
		return AppearanceResult(result);
	}

	[HttpDelete("{id}/appearances/{appearanceId}")]
	public async Task<IActionResult> RemoveAppearance(string id, string appearanceId, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var iconId) || !Guid.TryParse(appearanceId, out var assetId))
		{
			return AppearanceResult(Result.Fail<IconEntity, IconError>(IconError.NotFound));
		}

		return AppearanceResult(await _appearances.Remove(iconId, assetId, ct));
	}

	[HttpPost("{id}/appearances/merge")]
	public async Task<IActionResult> MergeAppearance(string id, MergeIconAppearanceRequest body, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var iconId) || !Guid.TryParse(body.IconId, out var sourceId))
		{
			return AppearanceResult(Result.Fail<IconEntity, IconError>(IconError.NotFound));
		}

		var result = await _appearances.Merge(iconId, sourceId, body.Key, ct);
		if (!result.Success)
		{
			return AppearanceResult(Result.Fail<IconEntity, IconError>(result.Error!.Value, result.ErrorMessage));
		}

		var response = new IconAppearanceResponse
		{
			Success = true,
			Icon = IconMapper.ToDto(result.Data!.Icon, _iconPackCache),
			MergedIconId = result.Data.MergedIconId.ToString()
		};
		return Ok(response);
	}

	private IActionResult AppearanceResult(Result<IconEntity, IconError> result)
	{
		if (result.Success)
		{
			return Ok(new IconAppearanceResponse
			{
				Success = true,
				Icon = IconMapper.ToDto(result.Data!, _iconPackCache)
			});
		}

		var status = result.Error switch
		{
			IconError.NotFound or IconError.PackNotFound => StatusCodes.Status404NotFound,
			IconError.PackReadOnly => StatusCodes.Status403Forbidden,
			IconError.ValidationError or IconError.UnsupportedFormat => StatusCodes.Status400BadRequest,
			_ => StatusCodes.Status500InternalServerError
		};

		return StatusCode(status,
			new IconAppearanceResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = result.Error.ToString()!,
					Message = result.ErrorMessage ?? string.Empty
				}
			});
	}

	[HttpGet("{id}/image")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<IActionResult> GetImage(string id,
		[FromQuery] int? size,
		[FromQuery(Name = "v")] string? version,
		CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var iconId))
		{
			return NotFound();
		}

		var acceptWebp = Request.Headers.Accept.Any(v => v is not null && v.Contains("image/webp"));
		var result = await _iconService.GetImage(iconId,
			size,
			acceptWebp,
			staticFrame: false,
			ct,
			IconAppearanceContextQuery.FromRequest(HttpContext));
		if (!result.Success)
		{
			Response.Headers.CacheControl = "no-store";
			return NotFound();
		}

		var image = result.Data!;
		Response.Headers.Vary = "Accept";

		// Pack upgrades replace bytes under the same id, so only a URL naming the current content may be
		// cached for good; a request without a known version (missing or empty v) revalidates instead.
		var unversioned = string.IsNullOrEmpty(version);
		if (!unversioned && !string.Equals(version, image.Version, StringComparison.Ordinal))
		{
			Response.Headers.CacheControl = "no-store";
			return File(image.Content, image.ContentType);
		}

		Response.Headers.CacheControl = unversioned ? "no-cache" : "private, max-age=31536000, immutable";
		Response.Headers.ETag = image.ETag;
		if (Request.Headers.IfNoneMatch.Any(v => v is not null && v.Contains(image.ETag)))
		{
			await image.Content.DisposeAsync();
			return StatusCode(StatusCodes.Status304NotModified);
		}

		return File(image.Content, image.ContentType);
	}
}
