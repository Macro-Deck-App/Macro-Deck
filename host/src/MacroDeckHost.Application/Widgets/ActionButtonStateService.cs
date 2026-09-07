using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Widgets;

public interface IActionButtonStateService
{
	Task<WidgetStateWriteResult> SetAsync(Guid widgetId, string stateId, CancellationToken cancellationToken = default);

	Task<WidgetStateWriteResult> AdvanceAsync(Guid widgetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Backs the explicit "Set Button State"/"Cycle Button State" actions (issue #612). Each write takes
/// the per-widget lock, validates and persists, then - only after releasing the lock - reconciles
/// inline so <c>vars.state</c> is committed and any <c>onStateChange</c> flow has already run by the
/// time this call returns.
/// </summary>
public sealed class ActionButtonStateService : IActionButtonStateService
{
	private readonly IFolderCache _folderCache;
	private readonly IWidgetDataWriteLock _writeLock;
	private readonly IWidgetService _widgetService;
	private readonly IWidgetStateReconciler _reconciler;

	public ActionButtonStateService(
		IFolderCache folderCache,
		IWidgetDataWriteLock writeLock,
		IWidgetService widgetService,
		IWidgetStateReconciler reconciler)
	{
		_folderCache = folderCache;
		_writeLock = writeLock;
		_widgetService = widgetService;
		_reconciler = reconciler;
	}

	public Task<WidgetStateWriteResult> SetAsync(Guid widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
		=> Write(widgetId,
			(data, model) =>
			{
				if (model.FindState(stateId) is null)
				{
					return (null, WidgetStateWriteError.UnknownState);
				}

				ActionButtonStateJson.SetActiveState(data, stateId);
				return (stateId, null);
			},
			cancellationToken);

	public Task<WidgetStateWriteResult> AdvanceAsync(Guid widgetId, CancellationToken cancellationToken = default)
		=> Write(widgetId,
			(data, _) => ActionButtonStateJson.AdvanceState(data) is { } next
				? (next, null)
				: (null, WidgetStateWriteError.UnknownState),
			cancellationToken);

	private async Task<WidgetStateWriteResult> Write(
		Guid widgetId,
		Func<JsonObject, ActionButtonStateModel, (string? NewStateId, WidgetStateWriteError? Error)> mutate,
		CancellationToken cancellationToken)
	{
		string? newStateId;

		using (await _writeLock.AcquireAsync(widgetId, cancellationToken))
		{
			var widget = FindWidget(widgetId);
			if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
			{
				return WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);
			}

			var data = ActionButtonStateJson.ParseDataBag(widget.Data);

			// Upgrade in place before mutating: a button not yet re-saved since the redesign still
			// carries the legacy shape, and AdvanceState needs the states array that only the upgrade
			// produces. Mutating the raw bag would refuse the write on exactly the buttons that
			// predate this feature.
			ActionButtonStateJson.Normalize(data);
			var model = ActionButtonStateModel.Read(data);
			if (!model.StateMode)
			{
				return WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);
			}

			// Refuse rather than silently no-op: the target's state is not this call's to set while
			// something else is authoritative for it.
			if (model.StateProvider is not null)
			{
				return WidgetStateWriteResult.Failed(WidgetStateWriteError.ProviderActive);
			}

			if (model.StateMapping is not null)
			{
				return WidgetStateWriteResult.Failed(WidgetStateWriteError.MappingActive);
			}

			var (mutatedStateId, error) = mutate(data, model);
			if (error is not null || mutatedStateId is null)
			{
				return WidgetStateWriteResult.Failed(error ?? WidgetStateWriteError.UnknownState);
			}

			// widget is the live cached entity, so a failed Update would otherwise leave the cache
			// holding a value that was never persisted - contradicting the "nothing is written in any
			// failure case" this method promises its callers.
			var previousData = widget.Data;
			widget.Data = data.ToJsonString();
			var result = await _widgetService.Update(widget);
			if (!result.Success)
			{
				widget.Data = previousData;
				return WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound);
			}

			newStateId = mutatedStateId;
		}

		// The lock is released before Reconcile runs, deliberately: the onStateChange flow it may fire
		// can contain a widget action that targets this same button and takes the same per-widget
		// lock, and reconciling while still holding it would deadlock that action against this call.
		// Reconciling inline (not just enqueuing) is what guarantees vars.state is committed, and the
		// flow has already run, before this call returns.
		await _reconciler.Reconcile(widgetId, cancellationToken);
		return WidgetStateWriteResult.Succeeded(newStateId);
	}

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
