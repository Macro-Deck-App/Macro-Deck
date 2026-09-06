using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Rendering;

public sealed class WidgetStateService : IWidgetStateService
{
	private static readonly TimeSpan _providerReadTimeout = TimeSpan.FromSeconds(3);

	private readonly IFolderCache _folderCache;
	private readonly IVariableTemplateRenderer _templateRenderer;
	private readonly IActionConditionEvaluator _evaluator;
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly WidgetDerivedStateStore _derivedStates;
	private readonly StartupReadiness _readiness;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public WidgetStateService(
		IFolderCache folderCache,
		IVariableTemplateRenderer templateRenderer,
		IActionConditionEvaluator evaluator,
		IIntegrationRegistry integrationRegistry,
		WidgetDerivedStateStore derivedStates,
		StartupReadiness readiness,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_folderCache = folderCache;
		_templateRenderer = templateRenderer;
		_evaluator = evaluator;
		_integrationRegistry = integrationRegistry;
		_derivedStates = derivedStates;
		_readiness = readiness;
		_optimisticStates = optimisticStates;
	}

	public async Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var widget = FindWidget(widgetId);
		if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
		{
			return null;
		}

		var model = ActionButtonStateModel.Read(widget.Data);
		if (!model.StateMode || model.States.Count == 0)
		{
			return null;
		}

		// The provider is checked first and, when present, is the sole authority: a mapping stashed
		// alongside it is never consulted, even when the provider cannot answer right now. Falling
		// through to the mapping whenever an integration hiccups would silently swap who is in charge
		// of the button every time that integration restarts.
		if (model.StateProvider is { } provider)
		{
			return await ResolveProvider(widgetId, widget, model, provider, cancellationToken);
		}

		if (model.StateMapping is { } mapping)
		{
			return await ResolveMapping(widgetId, model, mapping, cancellationToken);
		}

		return ResolveExplicit(model);
	}

	private static WidgetStateResolution ResolveExplicit(ActionButtonStateModel model)
	{
		var stateId = model.ActiveStateId is { } id && model.FindState(id) is not null ? id : model.States[0].Id;
		var entry = model.FindState(stateId)!;
		return new WidgetStateResolution(entry.Id, entry.Label, ToOptions(model.States), false);
	}

	private async Task<WidgetStateResolution> ResolveMapping(
		Guid widgetId,
		ActionButtonStateModel model,
		ActionButtonStateMapping mapping,
		CancellationToken cancellationToken)
	{
		var context = await _templateRenderer.CreateContextAsync(VariableScope.Widget, widgetId.ToString());

		foreach (var rule in mapping.Rules)
		{
			// A rule naming a state that no longer exists is not a match; evaluation continues with
			// the next rule rather than throwing away every rule after it.
			if (model.FindState(rule.StateId) is null)
			{
				continue;
			}

			if (_evaluator.EvaluateExpression(rule.When, context))
			{
				var matched = model.FindState(rule.StateId)!;
				return new WidgetStateResolution(matched.Id, matched.Label, ToOptions(model.States), false);
			}
		}

		var fallbackId = mapping.FallbackStateId is { } fallback && model.FindState(fallback) is not null
			? fallback
			: model.States[0].Id;
		var fallbackEntry = model.FindState(fallbackId)!;
		return new WidgetStateResolution(fallbackEntry.Id, fallbackEntry.Label, ToOptions(model.States), false);
	}

	private async Task<WidgetStateResolution> ResolveProvider(
		Guid widgetId,
		WidgetEntity widget,
		ActionButtonStateModel model,
		ActionButtonStateProvider provider,
		CancellationToken cancellationToken)
	{
		var resolved = FindProviderAction(widget, provider);
		var identity = resolved is null
			? null
			: new WidgetOptimisticStateIdentity(widgetId,
				provider.BlockId,
				resolved.Value.IntegrationId,
				resolved.Value.ActionId);
		var snapshot = await TryReadProviderSnapshot(resolved, cancellationToken);
		// Read after the asynchronous provider call so an action that completed while the provider was
		// answering wins over the stale snapshot that call may have captured.
		var optimisticSnapshot = identity is null ? null : _optimisticStates?.Read(identity);
		var optimistic = optimisticSnapshot?.State;

		// An id that cannot survive a save is worse than no answer: it would be adopted now, then
		// rewritten by the next Normalize, orphaning the appearance the user configured against it. So
		// a snapshot carrying one is unusable in the same way an empty or self-contradictory one is,
		// and the button holds its last known state instead. Conformance check MDC0310 tells a plugin
		// author about this at build time rather than leaving them to discover it here.
		var usable = snapshot is { States.Count: > 0 } &&
			snapshot.States.All(s => ActionButtonStateJson.IsValidId(s.Id)) &&
			(snapshot.ActiveStateId is null || snapshot.States.Any(s => s.Id == snapshot.ActiveStateId));

		if (!usable)
		{
			while (identity is not null && optimistic is not null)
			{
				if (!ActionButtonStateJson.IsValidId(optimistic.ExpectedStateId) ||
					!provider.States.Any(state => state.Id == optimistic.ExpectedStateId))
				{
					optimisticSnapshot = _optimisticStates!.Remove(identity, optimistic.Generation);
					optimistic = optimisticSnapshot.State;
					continue;
				}

				var expected = provider.States.First(state => state.Id == optimistic.ExpectedStateId);
				return new WidgetStateResolution(expected.Id,
					expected.Label,
					ToOptions(provider.States),
					false)
				{
					OptimisticState = optimisticSnapshot!.Version
				};
			}

			// Unavailable for any reason: hold the last known id, else the first of the cached
			// provider states, else the first of the stored states - the provider defines the
			// complete set and the host never invents a state it did not declare.
			var fallbackOptions = provider.States.Count > 0 ? ToOptions(provider.States) : ToOptions(model.States);
			var fallbackId = _derivedStates.TryGet(widgetId) ??
				(provider.States.Count > 0 ? provider.States[0].Id : null) ??
				model.States[0].Id;
			var fallbackLabel = fallbackOptions.FirstOrDefault(o => o.Id == fallbackId)?.Label ??
				model.FindState(fallbackId)?.Label ?? fallbackId;
			return new WidgetStateResolution(fallbackId, fallbackLabel, fallbackOptions, false)
			{
				OptimisticState = optimisticSnapshot?.Version
			};
		}

		var liveOptions = ToOptions(snapshot!.States);
		var setChanged = !SequenceEqualStates(provider.States, snapshot.States);
		var activeId = snapshot.ActiveStateId ?? snapshot.States[0].Id;
		var activeLabel = snapshot.States.First(s => s.Id == activeId).Label;
		while (identity is not null && optimistic is not null)
		{
			if (!ActionButtonStateJson.IsValidId(optimistic.ExpectedStateId) ||
				!snapshot.States.Any(state => state.Id == optimistic.ExpectedStateId))
			{
				optimisticSnapshot = _optimisticStates!.Remove(identity, optimistic.Generation);
				optimistic = optimisticSnapshot.State;
				continue;
			}
			else if (activeId == optimistic.ExpectedStateId)
			{
				optimisticSnapshot = _optimisticStates!.Confirm(identity, optimistic.Generation, activeId);
				optimistic = optimisticSnapshot.State;
				continue;
			}

			var expected = snapshot.States.First(state => state.Id == optimistic.ExpectedStateId);
			return new WidgetStateResolution(expected.Id, expected.Label, liveOptions, setChanged)
			{
				ProvidedStates = snapshot.States,
				OptimisticState = optimisticSnapshot!.Version
			};
		}

		return new WidgetStateResolution(activeId, activeLabel, liveOptions, setChanged)
		{
			ProvidedStates = snapshot.States,
			OptimisticState = optimisticSnapshot?.Version
		};
	}

	private static async Task<ActionStateSnapshot?> TryReadProviderSnapshot(
		(ActionBlock Block, IStateProviderActionDefinition Action, string IntegrationId, string ActionId)? resolved,
		CancellationToken cancellationToken)
	{
		if (resolved is null)
		{
			return null;
		}

		// Never invoked through the executor: the block also runs as an ordinary action when its
		// trigger fires, but reporting state must never itself perform the action's side effect.
		var parameters = ActionParameterConverter.ToNullable(resolved.Value.Block.Parameters
			.ToDictionary(p => p.Name, p => p.Value));

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(_providerReadTimeout);

		try
		{
			return await resolved.Value.Action.GetActionStateAsync(parameters, timeoutSource.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// The guard chain a state-provider block must pass before it is ever read: found and enabled,
	/// its integration registered and enabled, and its action a state provider. Shared by the snapshot
	/// read and by the poll-interval lookup the background poller uses, so the two can never disagree
	/// about whether a block is currently a usable provider.
	/// </summary>
	private (ActionBlock Block, IStateProviderActionDefinition Action, string IntegrationId, string ActionId)?
		FindProviderAction(
			WidgetEntity widget,
			ActionButtonStateProvider provider)
	{
		if (!ActionFlowJson.TryFindBlock(ActionFlowJson.ParseFlows(widget.Data), provider.BlockId, out var block) ||
			block is null ||
			block.Disabled)
		{
			return null;
		}

		var integrationId = provider.IntegrationId ?? block.IntegrationId;
		var actionId = provider.ActionId ?? block.ActionId;
		if (string.IsNullOrWhiteSpace(integrationId) || string.IsNullOrWhiteSpace(actionId))
		{
			return null;
		}

		var integrationExists = _integrationRegistry.Integrations
			.Any(integration => string.Equals(integration.Id, integrationId, StringComparison.Ordinal));
		if (!integrationExists || !_integrationRegistry.IsEnabled(integrationId))
		{
			return null;
		}

		return _integrationRegistry.FindAction(integrationId, actionId) is IStateProviderActionDefinition stateProvider
			? (block, stateProvider, integrationId, actionId)
			: null;
	}

	public TimeSpan? GetProviderPollInterval(Guid widgetId)
	{
		var widget = FindWidget(widgetId);
		if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
		{
			return null;
		}

		var model = ActionButtonStateModel.Read(widget.Data);
		if (model.StateProvider is not { } provider)
		{
			return null;
		}

		return FindProviderAction(widget, provider)?.Action.StatePollInterval;
	}

	private static bool SequenceEqualStates(
		IReadOnlyList<ActionButtonStateProviderOption> cached,
		IReadOnlyList<ActionStateDefinition> live)
	{
		if (cached.Count != live.Count)
		{
			return false;
		}

		for (var i = 0; i < cached.Count; i++)
		{
			if (!string.Equals(cached[i].Id, live[i].Id, StringComparison.Ordinal) ||
				cached[i].Label != live[i].Label)
			{
				return false;
			}
		}

		return true;
	}

	private static List<WidgetStateOption> ToOptions(IReadOnlyList<ActionButtonStateEntry> states)
		=> states.Select(s => new WidgetStateOption(s.Id, s.Label)).ToList();

	private static List<WidgetStateOption> ToOptions(IReadOnlyList<ActionButtonStateProviderOption> states)
		=> states.Select(s => new WidgetStateOption(s.Id, s.Label)).ToList();

	private static List<WidgetStateOption> ToOptions(IReadOnlyList<ActionStateDefinition> states)
		=> states.Select(s => new WidgetStateOption(s.Id, s.Label)).ToList();

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
