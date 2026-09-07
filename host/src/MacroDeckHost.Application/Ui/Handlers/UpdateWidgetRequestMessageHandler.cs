using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateWidgetRequestMessageHandler : IUiTransportMessageHandler<UpdateWidgetRequest, UpdateWidgetResponse>
{
	private readonly IWidgetService _widgetService;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly IFolderCache _folderCache;

	public UpdateWidgetRequestMessageHandler(IWidgetService widgetService,
		IWidgetDataSchemaProvider schemas,
		IFolderCache folderCache)
	{
		_widgetService = widgetService;
		_schemas = schemas;
		_folderCache = folderCache;
	}

	public async ValueTask<UpdateWidgetResponse> Handle(
		UpdateWidgetRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var widgetId) || !Guid.TryParse(request.FolderId, out var folderId))
		{
			return Fail("VALIDATION_ERROR", AppStrings.Errors.Widgets.InvalidWidgetOrFolderId());
		}

		var data = request.Data;

		// The editor save handler is one of the host-controlled writes that must run the legacy
		// upgrade and structural normalization - a plain save of an un-migrated toggle button has to
		// come out the other side in the current N-state shape, not stay stuck in the old one forever.
		if (request.Type == WidgetTypeIds.ActionButton && !string.IsNullOrWhiteSpace(data))
		{
			var bag = ActionButtonStateJson.ParseDataBag(data);
			ActionButtonStateJson.Normalize(bag);

			// Rejected outright rather than silently coerced: the previous stored mapping is left
			// byte-identical, exactly like a schema failure below.
			if (!ActionButtonStateJson.HasResolvableFallback(bag))
			{
				return Fail("VALIDATION_ERROR", AppStrings.Errors.Widgets.FallbackStateUnresolved());
			}

			if (GrowsPastStateLimit(bag, widgetId, folderId))
			{
				return Fail("VALIDATION_ERROR",
					AppStrings.Errors.Widgets.TooManyStates(max: ActionButtonStateModel.MaxStates));
			}

			data = bag.ToJsonString();
		}

		// The persisted payload is a final safety boundary behind the editor's client-side JSON Schema
		// check (issue #168) - this is the only place that gates it: WidgetService.Update is also reached
		// by the PATCH .../data runtime merge and by the public IWidgetApi.ApplyAsync SDK contract, and
		// validating there would break a plugin restyling a widget with older stored data.
		var validationError = ValidateData(request.Type, data);
		if (validationError is not null)
		{
			return Fail("VALIDATION_ERROR", validationError.Value);
		}

		var widget = new WidgetEntity
		{
			Id = widgetId,
			FolderId = folderId,
			Type = request.Type,
			PositionX = request.PositionX,
			PositionY = request.PositionY,
			Width = request.Width,
			Height = request.Height,
			Data = data
		};

		var result = await _widgetService.Update(widget);

		var response = new UpdateWidgetResponse { Success = result.Success };

		if (result.Success)
		{
			response.Widget = FolderDtoMapper.MapWidgetToDto(result.Data!);
		}
		else
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}

	private LocalizedText? ValidateData(string type, string? data)
	{
		if (string.IsNullOrWhiteSpace(data))
		{
			return null;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(data);
		}
		catch (JsonException)
		{
			return AppStrings.Errors.Widgets.DataNotValidJson();
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return AppStrings.Errors.Widgets.DataMustBeJsonObject();
			}

			if (!_schemas.TryGet(type, out var schema))
			{
				return null;
			}

			var problems = WidgetDataSchema.Validate(schema, document.RootElement);
			if (problems.Count == 0)
			{
				return null;
			}

			const int maxShown = 3;
			var shown = string.Join("; ",
				problems.Take(maxShown)
					.Select(p => $"{(string.IsNullOrEmpty(p.Pointer) ? "(root)" : p.Pointer)}: {p.Message}"));

			return problems.Count > maxShown
				? AppStrings.Errors.Widgets.DataValidationFailedWithMore(details: shown,
					more: problems.Count - maxShown)
				: AppStrings.Errors.Widgets.DataValidationFailed(details: shown);
		}
	}

	// Refuses growth only (issue #673): a button already over the limit still saves, so one predating the
	// limit stays repairable, and an adopted provider set is the provider's own and never blocked here.
	private bool GrowsPastStateLimit(JsonObject incoming, Guid widgetId, Guid folderId)
	{
		if (StateCount(incoming) <= ActionButtonStateModel.MaxStates ||
			ActionButtonStateModel.Read(incoming).StateProvider is not null)
		{
			return false;
		}

		var stored = _folderCache.GetFolderById(folderId)?.Widgets.FirstOrDefault(w => w.Id == widgetId);
		var storedBag = ActionButtonStateJson.ParseDataBag(stored?.Data);

		return StateCount(incoming) > Math.Max(StateCount(storedBag),
			StateCount(storedBag["manualStateBackup"] as JsonObject));
	}

	// The stored bag may still hold the legacy off/on object rather than an array, which counts as no
	// states at all: only the array form can carry a count this limit is about.
	private static int StateCount(JsonObject? data) => (data?["states"] as JsonArray)?.Count ?? 0;

	private static UpdateWidgetResponse Fail(string code, LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = code, Message = message }
		};
}
