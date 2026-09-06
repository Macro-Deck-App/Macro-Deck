using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateWidgetDataRequestMessageHandler
	: IUiTransportMessageHandler<UpdateWidgetDataRequest, UpdateWidgetDataResponse>
{
	// isToggled retired with the boolean toggle model (issue #612): a state-mode button no longer
	// flips on press, so nothing is left to merge here. The mechanism (and the IFlowExecutor/ILogger
	// dependencies below) are kept in place rather than torn out, since a future runtime-patchable key
	// would need exactly this shape again; the onStateChange firing that used to happen from this
	// handler moved to WidgetStateReconciler, which is now the sole firing site.
	private static readonly HashSet<string> _runtimeDataKeys = [];

	private readonly IFolderCache _folderCache;
	private readonly IWidgetService _widgetService;
	private readonly IFlowExecutor _flowExecutor;
	private readonly IWidgetDataWriteLock _writeLock;
	private readonly ILogger _logger;

	public UpdateWidgetDataRequestMessageHandler(
		IFolderCache folderCache,
		IWidgetService widgetService,
		IFlowExecutor flowExecutor,
		IWidgetDataWriteLock writeLock,
		ILogger logger)
	{
		_folderCache = folderCache;
		_widgetService = widgetService;
		_flowExecutor = flowExecutor;
		_writeLock = writeLock;
		_logger = logger.ForContext<UpdateWidgetDataRequestMessageHandler>();
	}

	public async ValueTask<UpdateWidgetDataResponse> Handle(
		UpdateWidgetDataRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId) || !Guid.TryParse(request.FolderId, out var folderId))
		{
			return Fail("VALIDATION_ERROR", "Invalid widget or folder ID");
		}

		var widget = _folderCache.GetFolderById(folderId)?.Widgets.FirstOrDefault(w => w.Id == widgetId);
		if (widget is null)
		{
			return Fail("NOT_FOUND", "Widget not found");
		}

		JsonObject updates;
		try
		{
			updates = JsonNode.Parse(request.Data) as JsonObject ?? [];
		}
		catch (JsonException)
		{
			return Fail("VALIDATION_ERROR", "Data must be a JSON object");
		}

		Result<WidgetEntity, WidgetError> result;

		using (await _writeLock.AcquireAsync(widgetId, cancellationToken))
		{
			var data = ParseDataBag(widget.Data);

			foreach (var (key, value) in updates)
			{
				if (!_runtimeDataKeys.Contains(key))
				{
					continue;
				}

				data[key] = value?.DeepClone();
			}

			widget.Data = data.ToJsonString();

			result = await _widgetService.Update(widget);
		}

		if (!result.Success || result.Data is null)
		{
			return Fail(result.Error.ToString() ?? "ERROR", result.ErrorMessage ?? string.Empty);
		}

		return new UpdateWidgetDataResponse
		{
			Success = true,
			Widget = new Widget
			{
				Id = result.Data.Id.ToString(),
				Type = result.Data.Type,
				PositionX = result.Data.PositionX,
				PositionY = result.Data.PositionY,
				Width = result.Data.Width,
				Height = result.Data.Height,
				Data = result.Data.Data
			}
		};
	}

	private static JsonObject ParseDataBag(string? json)
	{
		if (string.IsNullOrEmpty(json))
		{
			return [];
		}

		try
		{
			return JsonNode.Parse(json) as JsonObject ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private static UpdateWidgetDataResponse Fail(string code, string message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = code, Message = message }
		};
}
