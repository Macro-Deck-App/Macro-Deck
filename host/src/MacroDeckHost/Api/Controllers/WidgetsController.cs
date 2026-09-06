using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Portable;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/widgets")]
public class WidgetsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<CreateWidgetRequest, CreateWidgetResponse> _createWidget;

	private readonly IUiTransportMessageHandler<CreateWidgetFromApplicationRequest, CreateWidgetResponse>
		_createWidgetFromApplication;

	private readonly IUiTransportMessageHandler<UpdateWidgetRequest, UpdateWidgetResponse> _updateWidget;

	private readonly IUiTransportMessageHandler<UpdateWidgetPositionsRequest, UpdateWidgetPositionsResponse>
		_updateWidgetPositions;

	private readonly IUiTransportMessageHandler<UpdateWidgetStateRequest, UpdateWidgetStateResponse> _updateWidgetState;
	private readonly IUiTransportMessageHandler<UpdateWidgetDataRequest, UpdateWidgetDataResponse> _updateWidgetData;
	private readonly IUiTransportMessageHandler<DeleteWidgetRequest, DeleteWidgetResponse> _deleteWidget;
	private readonly IUiTransportMessageHandler<SetWidgetPinnedRequest, SetWidgetPinnedResponse> _setWidgetPinned;

	private readonly IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse>
		_getWidgetDataSchemas;

	private readonly IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse> _getWidgetTypes;

	private readonly IUiTransportMessageHandler<CreateWidgetsRequest, CreateWidgetsResponse> _createWidgets;
	private readonly IUiTransportMessageHandler<DeleteWidgetsRequest, DeleteWidgetsResponse> _deleteWidgets;

	private readonly IUiTransportMessageHandler<SetWidgetsPinnedRequest, SetWidgetsPinnedResponse>
		_setWidgetsPinned;

	private readonly IWidgetPortabilityService _portability;

	public WidgetsController(
		IUiTransportMessageHandler<CreateWidgetRequest, CreateWidgetResponse> createWidget,
		IUiTransportMessageHandler<CreateWidgetFromApplicationRequest, CreateWidgetResponse>
			createWidgetFromApplication,
		IUiTransportMessageHandler<UpdateWidgetRequest, UpdateWidgetResponse> updateWidget,
		IUiTransportMessageHandler<UpdateWidgetPositionsRequest, UpdateWidgetPositionsResponse> updateWidgetPositions,
		IUiTransportMessageHandler<UpdateWidgetStateRequest, UpdateWidgetStateResponse> updateWidgetState,
		IUiTransportMessageHandler<UpdateWidgetDataRequest, UpdateWidgetDataResponse> updateWidgetData,
		IUiTransportMessageHandler<DeleteWidgetRequest, DeleteWidgetResponse> deleteWidget,
		IUiTransportMessageHandler<SetWidgetPinnedRequest, SetWidgetPinnedResponse> setWidgetPinned,
		IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse> getWidgetDataSchemas,
		IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse> getWidgetTypes,
		IUiTransportMessageHandler<CreateWidgetsRequest, CreateWidgetsResponse> createWidgets,
		IUiTransportMessageHandler<DeleteWidgetsRequest, DeleteWidgetsResponse> deleteWidgets,
		IUiTransportMessageHandler<SetWidgetsPinnedRequest, SetWidgetsPinnedResponse> setWidgetsPinned,
		IWidgetPortabilityService portability)
	{
		_createWidget = createWidget;
		_createWidgetFromApplication = createWidgetFromApplication;
		_updateWidget = updateWidget;
		_updateWidgetPositions = updateWidgetPositions;
		_updateWidgetState = updateWidgetState;
		_updateWidgetData = updateWidgetData;
		_deleteWidget = deleteWidget;
		_setWidgetPinned = setWidgetPinned;
		_getWidgetDataSchemas = getWidgetDataSchemas;
		_getWidgetTypes = getWidgetTypes;
		_createWidgets = createWidgets;
		_deleteWidgets = deleteWidgets;
		_setWidgetsPinned = setWidgetsPinned;
		_portability = portability;
	}

	[HttpGet("schemas")]
	public Task<GetWidgetDataSchemasResponse> GetDataSchemas(CancellationToken ct)
		=> _getWidgetDataSchemas.Handle(new GetWidgetDataSchemasRequest(), ct).AsTask();

	[HttpGet("types")]
	public Task<GetWidgetTypesResponse> GetTypes(CancellationToken ct)
		=> _getWidgetTypes.Handle(new GetWidgetTypesRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateWidgetResponse> Create(CreateWidgetRequest body, CancellationToken ct)
		=> _createWidget.Handle(body, ct).AsTask();

	[HttpPost("batch")]
	public Task<CreateWidgetsResponse> CreateMany(CreateWidgetsRequest body, CancellationToken ct)
		=> _createWidgets.Handle(body, ct).AsTask();

	[HttpPost("from-application")]
	public Task<CreateWidgetResponse> CreateFromApplication(CreateWidgetFromApplicationRequest body,
		CancellationToken ct)
		=> _createWidgetFromApplication.Handle(body, ct).AsTask();

	[HttpPut]
	public Task<UpdateWidgetResponse> Update(UpdateWidgetRequest body, CancellationToken ct)
		=> _updateWidget.Handle(body, ct).AsTask();

	[HttpPut("positions")]
	public Task<UpdateWidgetPositionsResponse> UpdatePositions(UpdateWidgetPositionsRequest body, CancellationToken ct)
		=> _updateWidgetPositions.Handle(body, ct).AsTask();

	[HttpPatch("{widgetId}/state")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<UpdateWidgetStateResponse> UpdateState(string widgetId,
		UpdateWidgetStateRequest body,
		CancellationToken ct)
	{
		body.WidgetId = widgetId;
		return _updateWidgetState.Handle(body, ct).AsTask();
	}

	[HttpPatch("{widgetId}/data")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<UpdateWidgetDataResponse> UpdateData(string widgetId,
		UpdateWidgetDataRequest body,
		CancellationToken ct)
	{
		body.WidgetId = widgetId;
		return _updateWidgetData.Handle(body, ct).AsTask();
	}

	[HttpPut("{widgetId}/pinned")]
	public Task<SetWidgetPinnedResponse> SetPinned(string widgetId,
		SetWidgetPinnedRequest body,
		CancellationToken ct)
	{
		body.WidgetId = widgetId;
		return _setWidgetPinned.Handle(body, ct).AsTask();
	}

	[HttpPut("pinned/batch")]
	public Task<SetWidgetsPinnedResponse> SetPinnedMany(SetWidgetsPinnedRequest body, CancellationToken ct)
		=> _setWidgetsPinned.Handle(body, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteWidgetResponse> Delete(string id, [FromQuery] string folderId, CancellationToken ct)
		=> _deleteWidget.Handle(new DeleteWidgetRequest { Id = id, FolderId = folderId }, ct).AsTask();

	[HttpPost("batch/delete")]
	public Task<DeleteWidgetsResponse> DeleteMany(DeleteWidgetsRequest body, CancellationToken ct)
		=> _deleteWidgets.Handle(body, ct).AsTask();

	[HttpPost("export")]
	public async Task<IActionResult> Export(ExportWidgetsRequest body, CancellationToken ct)
	{
		if (!Guid.TryParse(body.FolderId, out var folderId))
		{
			return NotFound();
		}

		var widgetIds = new List<Guid>();
		foreach (var raw in body.WidgetIds)
		{
			if (Guid.TryParse(raw, out var widgetId))
			{
				widgetIds.Add(widgetId);
			}
		}

		var result = await _portability.Export(folderId, widgetIds, body.ToOptions(), ct);
		if (!result.Success)
		{
			return PortabilityHttp.ToErrorResult(this, result.Error!.Value, result.ErrorMessage);
		}

		var fileName = "widgets" + PortableFileExtensions.Widgets;
		return File(result.Data!, "application/zip", fileName);
	}

	[HttpPost("import")]
	[RequestSizeLimit(PortabilityHttp.MaxRequestBytes)]
	public async Task<ImportWidgetsResponse> Import([FromForm] IFormFile? file,
		[FromForm] string folderId,
		[FromForm] int anchorX,
		[FromForm] int anchorY,
		[FromForm] string? password,
		CancellationToken ct)
	{
		if (!Guid.TryParse(folderId, out var targetFolderId))
		{
			return Fail(Domain.Enums.PortabilityError.ValidationError, "A valid folder id is required");
		}

		var bytes = await ArchivePathReader.ReadUpload(file, ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(targetFolderId, anchorX, anchorY, bytes.Data!, password, ct);
	}

	[HttpPost("import-path")]
	public async Task<ImportWidgetsResponse> ImportFromPath(ImportWidgetsFromPathRequest body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new ImportWidgetsResponse { Success = false, Error = refusal };
		}

		if (!Guid.TryParse(body.FolderId, out var targetFolderId))
		{
			return Fail(Domain.Enums.PortabilityError.ValidationError, "A valid folder id is required");
		}

		var bytes = await ArchivePathReader.Read(body.Path,
			[PortableFileExtensions.Widgets],
			PortabilityHttp.MaxRequestBytes,
			ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(targetFolderId, body.AnchorX, body.AnchorY, bytes.Data!, body.Password, ct);
	}

	private async Task<ImportWidgetsResponse> Import(Guid folderId,
		int anchorX,
		int anchorY,
		byte[] archiveBytes,
		string? password,
		CancellationToken ct)
	{
		var result = await _portability.Import(folderId, anchorX, anchorY, archiveBytes, password, ct);
		if (!result.Success)
		{
			return Fail(result.Error!.Value, result.ErrorMessage);
		}

		return new ImportWidgetsResponse
		{
			Success = true,
			Widgets = result.Data!.Select(widget => FolderDtoMapper.MapWidgetToDto(widget)).ToList()
		};
	}

	private static ImportWidgetsResponse Fail(Domain.Enums.PortabilityError error, string? message)
		=> new()
		{
			Success = false,
			Error = PortabilityHttp.ToTransportError(error, message)
		};
}
