using System.Collections.Concurrent;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Rendering;

public interface IWidgetStateReconciler
{
	Task<WidgetStateReconciliation> Reconcile(Guid widgetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <paramref name="Changed" /> drives whether the push fires at all (the first value seen counts as
/// changed, since nothing has been shown yet); <paramref name="Transitioned" /> drives whether
/// <c>onStateChange</c> fires (never on the seed); <paramref name="SetChanged" /> drives whether the
/// push carries <see cref="States" /> (resolution: included on subscribe and whenever the set
/// changed, omitted on a plain transition).
/// </summary>
public sealed record WidgetStateReconciliation(
	string? StateId,
	LocalizedText StateLabel,
	IReadOnlyList<WidgetStateOption> States,
	bool Changed,
	bool Transitioned,
	bool SetChanged)
{
	public static readonly WidgetStateReconciliation None = new(null, null, [], false, false, false);

	public WidgetOptimisticStateVersion? OptimisticState { get; init; }
}

public sealed class WidgetStateReconciler : IWidgetStateReconciler
{
	public const string StateVariableName = "state";
	public const string StateLabelVariableName = "state_label";

	// An onStateChange flow that sets its own button re-enters Reconcile from inside FireStateChange.
	// Left unbounded, a flow that always lands on a different state than it started from would recurse
	// forever; this caps the chain and lets the state/variables settle instead of firing indefinitely.
	private const int MaxReconcileDepth = 5;

	private static readonly ConcurrentDictionary<Guid, int> _reconcileDepth = new();

	private readonly IWidgetStateService _stateService;
	private readonly WidgetDerivedStateStore _store;
	private readonly IVariableService _variableService;
	private readonly IFlowExecutor _flowExecutor;
	private readonly IFolderCache _folderCache;
	private readonly IWidgetService _widgetService;
	private readonly IWidgetDataWriteLock _writeLock;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public WidgetStateReconciler(
		IWidgetStateService stateService,
		WidgetDerivedStateStore store,
		IVariableService variableService,
		IFlowExecutor flowExecutor,
		IFolderCache folderCache,
		IWidgetService widgetService,
		IWidgetDataWriteLock writeLock,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		ILogger logger,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_stateService = stateService;
		_store = store;
		_variableService = variableService;
		_flowExecutor = flowExecutor;
		_folderCache = folderCache;
		_widgetService = widgetService;
		_writeLock = writeLock;
		_preferences = preferences;
		_localization = localization;
		_logger = logger.ForContext<WidgetStateReconciler>();
		_optimisticStates = optimisticStates;
	}

	public async Task<WidgetStateReconciliation> Reconcile(Guid widgetId, CancellationToken cancellationToken = default)
	{
		var resolution = await _stateService.Resolve(widgetId, cancellationToken);
		if (resolution is null)
		{
			_store.Remove(widgetId);

			// A button that leaves state mode has no active state, so its state variables must go with
			// it - otherwise a flow keeps resolving a name the button no longer has any value for.
			var removedScopeRefId = widgetId.ToString();
			await _variableService.RemoveWidgetVariable(VariableScope.Widget, removedScopeRefId, StateVariableName);
			await _variableService.RemoveWidgetVariable(VariableScope.Widget,
				removedScopeRefId,
				StateLabelVariableName);
			return WidgetStateReconciliation.None;
		}

		if (!IsCurrent(resolution))
		{
			return await Reconcile(widgetId, cancellationToken);
		}

		var scopeRefId = widgetId.ToString();
		await _variableService.UpsertWidgetVariable(VariableScope.Widget,
			scopeRefId,
			StateVariableName,
			VariableType.Text,
			resolution.StateId);
		// A variable value is stored text a flow reads back, so the label is resolved here rather than
		// stored as a reference.
		var settings = await _preferences.GetLocalization();
		await _variableService.UpsertWidgetVariable(VariableScope.Widget,
			scopeRefId,
			StateLabelVariableName,
			VariableType.Text,
			_localization.Resolve(resolution.StateLabel, settings.Culture));

		if (resolution.ProviderSetChanged)
		{
			await AdoptProviderStates(widgetId, resolution, cancellationToken);
		}

		if (!IsCurrent(resolution))
		{
			return await Reconcile(widgetId, cancellationToken);
		}

		var previous = _store.GetAndSet(widgetId, resolution.StateId);
		var transitioned = previous is not null && previous != resolution.StateId;
		var changed = previous != resolution.StateId;

		if (transitioned)
		{
			var depth = _reconcileDepth.AddOrUpdate(widgetId, 1, static (_, d) => d + 1);
			try
			{
				if (depth <= MaxReconcileDepth)
				{
					await FireStateChange(widgetId, resolution.StateId, previous, cancellationToken);
				}
				else
				{
					_logger.Warning(
						"onStateChange flow for widget {WidgetId} suppressed after {Depth} nested transitions",
						widgetId,
						depth);
				}
			}
			finally
			{
				_reconcileDepth.AddOrUpdate(widgetId, 0, static (_, d) => Math.Max(0, d - 1));
			}
		}

		return new WidgetStateReconciliation(resolution.StateId,
			resolution.StateLabel,
			resolution.States,
			changed,
			transitioned,
			resolution.ProviderSetChanged)
		{
			OptimisticState = resolution.OptimisticState
		};
	}

	private bool IsCurrent(WidgetStateResolution resolution)
		=> resolution.OptimisticState is null ||
			_optimisticStates?.IsCurrent(resolution.OptimisticState) != false;

	private async Task AdoptProviderStates(Guid widgetId,
		WidgetStateResolution resolution,
		CancellationToken cancellationToken)
	{
		var widget = FindWidget(widgetId);
		if (widget is null)
		{
			return;
		}

		using var _ = await _writeLock.AcquireAsync(widgetId, cancellationToken);

		widget = FindWidget(widgetId);
		if (widget is null)
		{
			return;
		}

		var data = ActionButtonStateJson.ParseDataBag(widget.Data);
		var culture = (await _preferences.GetLocalization()).Culture;
		var changed = ActionButtonStateJson.AdoptProviderStates(data,
			resolution.ProvidedStates,
			_localization,
			culture);
		if (!changed)
		{
			return;
		}

		widget.Data = data.ToJsonString();
		var result = await _widgetService.Update(widget);
		if (!result.Success)
		{
			_logger.Warning("Failed to persist refreshed provider states for widget {WidgetId}: {Error}",
				widgetId,
				result.ErrorMessage ?? result.Error.ToString());
		}
	}

	private async Task FireStateChange(Guid widgetId,
		string stateId,
		string? previousStateId,
		CancellationToken cancellationToken)
	{
		var widget = FindWidget(widgetId);
		if (widget is null)
		{
			return;
		}

		// The state changed because a variable, a poll, or an explicit-state action moved it - never
		// because of a client press - so this runs host-initiated and skips the host-lock gate.
		var result = await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widget.Data,
				Trigger = TriggerSelector.ByType(WidgetTriggerTypes.StateChange),
				Scope = VariableScope.Widget,
				ScopeRefId = widgetId.ToString(),
				OwnerWidgetId = widgetId,
				Origin = ExecutionOrigin.Host,
				EventParameters = new Dictionary<string, object?>
				{
					["state"] = stateId,
					["previousState"] = previousStateId
				}
			},
			cancellationToken);

		if (result.Status != FlowExecutionStatus.Succeeded)
		{
			_logger.Warning(
				"onStateChange flow for widget {WidgetId} finished as {Status} (execution {ExecutionId}): " +
				"{ErrorCode} {ErrorMessage}",
				widgetId,
				result.Status,
				result.ExecutionId,
				result.ErrorCode,
				result.ErrorMessage.ToString());
		}
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
