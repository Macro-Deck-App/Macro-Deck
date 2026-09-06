using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Rendering;

public sealed class LabelTextService : ILabelTextService
{
	private readonly IFolderCache _folderCache;
	private readonly IVariableTemplateRenderer _templateRenderer;
	private readonly StartupReadiness _readiness;

	public LabelTextService(
		IFolderCache folderCache,
		IVariableTemplateRenderer templateRenderer,
		StartupReadiness readiness)
	{
		_folderCache = folderCache;
		_templateRenderer = templateRenderer;
		_readiness = readiness;
	}

	public async Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		state = LabelGroups.Normalize(state);

		var widget = FindWidget(widgetId);
		if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
		{
			return null;
		}

		var model = ActionButtonStateModel.Read(widget.Data);
		var rootLabel = ReadString(model.Data, "label");

		// An unknown id, and stateMode off entirely, both fall back to the root label - the same
		// fallback a state with no label of its own uses.
		var stateLabel = model.StateMode ? ReadString(model.FindState(state)?.Appearance, "label") : null;
		var labelText = !string.IsNullOrEmpty(stateLabel) ? stateLabel : rootLabel;
		if (string.IsNullOrEmpty(labelText))
		{
			return null;
		}

		var resolved = await _templateRenderer.RenderAsync(labelText, VariableScope.Widget, widgetId.ToString());
		return string.IsNullOrEmpty(resolved) ? null : resolved;
	}

	public async Task<string?> ResolvePreview(LabelImagePreviewRequest request,
		CancellationToken cancellationToken = default)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var label = request.Label ?? string.Empty;
		var scope = string.IsNullOrEmpty(request.ScopeRefId) ? VariableScope.Global : VariableScope.Widget;
		var resolved = await _templateRenderer.RenderAsync(label, scope, request.ScopeRefId);
		return string.IsNullOrEmpty(resolved) ? null : resolved;
	}

	private static string? ReadString(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private WidgetEntity? FindWidget(Guid widgetId)
	{
		foreach (var folder in _folderCache.GetAllFolders())
		{
			var widget = folder.Widgets.FirstOrDefault(w => w.Id == widgetId);
			if (widget is not null)
			{
				return widget;
			}
		}

		return null;
	}
}
