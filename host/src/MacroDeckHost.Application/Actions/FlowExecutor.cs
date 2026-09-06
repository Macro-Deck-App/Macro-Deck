using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeckHost.Application.Actions;

public interface IFlowExecutor
{
	Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class FlowExecutor : IFlowExecutor
{
	private const int MaxWhileIterations = 1000;

	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IVariableTemplateRenderer _variableRenderer;
	private readonly IActionConditionEvaluator _conditionEvaluator;
	private readonly ISecretService _secretService;
	private readonly IActionInteractions _interactions;
	private readonly IUiInteractionsFactory _uiInteractions;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly IMusicPlayerPollNudge _musicPlayerPollNudge;
	private readonly IHostLockState _lockState;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;
	private readonly WidgetOptimisticStateStore? _optimisticStates;
	private readonly WidgetStateEvalChannel? _stateEvalQueue;

	public FlowExecutor(
		IIntegrationRegistry integrationRegistry,
		IVariableTemplateRenderer variableRenderer,
		IActionConditionEvaluator conditionEvaluator,
		ISecretService secretService,
		IActionInteractions interactions,
		IUiInteractionsFactory uiInteractions,
		IUserNotificationStore userNotificationStore,
		IMusicPlayerPollNudge musicPlayerPollNudge,
		IHostLockState lockState,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		ILogger logger,
		WidgetOptimisticStateStore? optimisticStates = null,
		WidgetStateEvalChannel? stateEvalQueue = null)
	{
		_integrationRegistry = integrationRegistry;
		_variableRenderer = variableRenderer;
		_conditionEvaluator = conditionEvaluator;
		_secretService = secretService;
		_interactions = interactions;
		_uiInteractions = uiInteractions;
		_userNotificationStore = userNotificationStore;
		_musicPlayerPollNudge = musicPlayerPollNudge;
		_lockState = lockState;
		_preferences = preferences;
		_localization = localization;
		_logger = logger.ForContext<FlowExecutor>();
		_optimisticStates = optimisticStates;
		_stateEvalQueue = stateEvalQueue;
	}

	public async Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request,
		CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();

		// Host-autonomous triggers (schedules, startup/server-lifecycle events, in-process integration
		// events, and the widget-state reconciler) mark their requests Origin = Host and deliberately
		// skip this gate - only externally triggered execution is blocked while locked. The gate is
		// entry-only: a flow already running when the lock happens keeps running to completion.
		if (request.Origin == ExecutionOrigin.Client && _lockState.IsLocked)
		{
			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Failed,
				DurationMs = stopwatch.ElapsedMilliseconds,
				ErrorCode = ActionExecutionErrorCodes.HostLocked,
				ErrorMessage = AppStrings.Errors.Common.HostLocked()
			};
		}

		List<ActionFlow> matchingFlows;
		try
		{
			matchingFlows = SelectFlows(ActionFlowJson.ParseFlows(request.FlowsSource), request.Trigger);
		}
		catch (JsonException ex)
		{
			_logger.Warning(ex, "Failed to parse stored flows for trigger {Trigger}", request.Trigger.Value);
			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Failed,
				DurationMs = stopwatch.ElapsedMilliseconds,
				ErrorCode = ActionExecutionErrorCodes.FlowParseError,
				ErrorMessage = AppStrings.Errors.Actions.FlowUnreadable()
			};
		}

		if (matchingFlows.Count == 0)
		{
			_logger.Debug("No flows found for trigger {Trigger}", request.Trigger.Value);
			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				DurationMs = stopwatch.ElapsedMilliseconds,
				MatchedFlows = 0
			};
		}

		var variableContext = await _variableRenderer.CreateContextAsync(request.Scope, request.ScopeRefId);
		if (request.EventParameters is { Count: > 0 })
		{
			variableContext = variableContext.WithEvent(request.EventParameters);
		}

		if (request.ScriptInputs is { Count: > 0 })
		{
			variableContext = variableContext.WithInputs(request.ScriptInputs);
		}

		var run = new FlowRun(request.ExecutionId,
			variableContext,
			request.OriginClientId,
			request.OwnerWidgetId);

		try
		{
			await Task.WhenAll(matchingFlows.Select(flow => ExecuteFlowAsync(flow, run, cancellationToken)));
		}
		catch (OperationCanceledException)
		{
			return BuildResult(run, FlowExecutionStatus.Cancelled, matchingFlows.Count, stopwatch.ElapsedMilliseconds);
		}

		return BuildResult(run, AggregateStatus(run.Outcomes), matchingFlows.Count, stopwatch.ElapsedMilliseconds);
	}

	private static FlowExecutionResult BuildResult(
		FlowRun run,
		FlowExecutionStatus status,
		int matchedFlows,
		long durationMs)
	{
		var firstFailure = run.Outcomes.FirstOrDefault(o => o.Status == ActionOutcomeStatus.Failed);
		var errorCode = firstFailure?.ErrorCode;
		var errorMessage = firstFailure?.ErrorMessage ?? default;
		if (errorCode is null && status == FlowExecutionStatus.Cancelled)
		{
			errorCode = ActionExecutionErrorCodes.Cancelled;
			errorMessage = AppStrings.Errors.Actions.Cancelled();
		}

		return new FlowExecutionResult
		{
			ExecutionId = run.ExecutionId,
			Status = status,
			DurationMs = durationMs,
			MatchedFlows = matchedFlows,
			Actions = run.Outcomes,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage
		};
	}

	private static FlowExecutionStatus AggregateStatus(IReadOnlyList<ActionExecutionOutcome> outcomes)
	{
		var attempted = outcomes.Count(o => o.Status is ActionOutcomeStatus.Succeeded
			or ActionOutcomeStatus.Accepted
			or ActionOutcomeStatus.Failed);
		var failed = outcomes.Count(o => o.Status == ActionOutcomeStatus.Failed);

		if (failed == 0)
		{
			return FlowExecutionStatus.Succeeded;
		}

		return failed == attempted ? FlowExecutionStatus.Failed : FlowExecutionStatus.PartiallyFailed;
	}

	private static List<ActionFlow> SelectFlows(List<ActionFlow> flows, TriggerSelector selector)
	{
		if (selector.ByTriggerId)
		{
			var flow = flows.FirstOrDefault(f => string.Equals(f.TriggerId, selector.Value, StringComparison.Ordinal));
			return flow is null ? [] : [flow];
		}

		return flows
			.Where(flow => string.Equals(flow.TriggerType, selector.Value, StringComparison.OrdinalIgnoreCase))
			.GroupBy(flow => flow.TriggerType, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.ToList();
	}

	private async Task ExecuteFlowAsync(ActionFlow flow, FlowRun run, CancellationToken cancellationToken)
	{
		try
		{
			await ExecuteBlocksAsync(flow.Children, run, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (BreakLoopException)
		{
			run.Record(flow.TriggerId,
				flow.TriggerLabel,
				null,
				null,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.UnsupportedBlock,
				AppStrings.Errors.Actions.BreakOutsideLoop(),
				0);
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Failed to execute action-button flow {TriggerId} ({TriggerType})",
				flow.TriggerId,
				flow.TriggerType);
			run.Record(flow.TriggerId,
				flow.TriggerLabel,
				null,
				null,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.FlowError,
				"The flow failed unexpectedly.",
				0);
		}
	}

	private async Task ExecuteBlocksAsync(
		IEnumerable<ActionBlock> blocks,
		FlowRun run,
		CancellationToken cancellationToken)
	{
		foreach (var block in blocks)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// A block switched off in the action builder is skipped whole, subtree included - a
			// disabled container must not run its body either. Not a failure - the block was never
			// meant to run.
			if (block.Disabled)
			{
				_logger.Debug("Skipping disabled block {BlockId} ({BlockType})", block.Id, block.BlockType);
				run.Record(block.Id,
					block.Label,
					block.IntegrationId,
					block.ActionId,
					ActionOutcomeStatus.Skipped,
					null,
					null,
					0);
				continue;
			}

			await ExecuteBlockAsync(block, run, cancellationToken);
		}
	}

	private async Task ExecuteBlockAsync(
		ActionBlock block,
		FlowRun run,
		CancellationToken cancellationToken)
	{
		switch (block.Type.ToLowerInvariant())
		{
			case "action":
				await ExecuteIntegrationActionAsync(block, run, cancellationToken);
				break;
			case "delay":
				await ExecuteDelayAsync(block, run.Variables, cancellationToken);
				break;
			case "condition":
				await ExecuteConditionAsync(block, run, cancellationToken);
				break;
			case "loop":
				await ExecuteLoopAsync(block, run, cancellationToken);
				break;
			case "flow-control":
				ExecuteFlowControl(block, run);
				break;
			case "trigger":
				await ExecuteBlocksAsync(block.Children, run, cancellationToken);
				break;
			default:
				if (!string.IsNullOrWhiteSpace(block.IntegrationId) || IsIntegrationActionBlockType(block.BlockType))
				{
					await ExecuteIntegrationActionAsync(block, run, cancellationToken);
				}
				else
				{
					_logger.Warning("Skipping unsupported action-button block {BlockType} ({Type})",
						block.BlockType,
						block.Type);
					run.Record(block.Id,
						block.Label,
						block.IntegrationId,
						block.ActionId,
						ActionOutcomeStatus.Failed,
						ActionExecutionErrorCodes.UnsupportedBlock,
						AppStrings.Errors.Actions.StepTypeNotSupported(),
						0);
				}

				break;
		}
	}

	private void ExecuteFlowControl(ActionBlock block, FlowRun run)
	{
		switch (block.BlockType)
		{
			case "break":
				throw new BreakLoopException();
			default:
				_logger.Warning("Skipping unsupported flow-control block {BlockType}", block.BlockType);
				run.Record(block.Id,
					block.Label,
					block.IntegrationId,
					block.ActionId,
					ActionOutcomeStatus.Failed,
					ActionExecutionErrorCodes.UnsupportedBlock,
					AppStrings.Errors.Actions.StepTypeNotSupported(),
					0);
				break;
		}
	}

	private async Task ExecuteIntegrationActionAsync(
		ActionBlock block,
		FlowRun run,
		CancellationToken cancellationToken)
	{
		var (integrationId, actionId) = ResolveActionIdentity(block);
		if (string.IsNullOrWhiteSpace(integrationId) || string.IsNullOrWhiteSpace(actionId))
		{
			_logger.Warning("Skipping action block {BlockType}: missing integration/action id", block.BlockType);
			run.Record(block.Id,
				block.Label,
				integrationId,
				actionId,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.InvalidBlock,
				AppStrings.Errors.Actions.MissingIntegrationOrAction(),
				0);
			return;
		}

		// IsEnabled alone cannot tell "disabled" apart from "no such integration" (a stored flow can
		// reference one that was since removed), so the registered-integrations list is checked first.
		var integrationExists = _integrationRegistry.Integrations
			.Any(integration => string.Equals(integration.Id, integrationId, StringComparison.Ordinal));
		if (!integrationExists)
		{
			_logger.Warning("Skipping action {IntegrationId}.{ActionId}: integration not found",
				integrationId,
				actionId);
			run.Record(block.Id,
				block.Label,
				integrationId,
				actionId,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.IntegrationNotFound,
				AppStrings.Errors.Actions.StepIntegrationGone(),
				0);
			return;
		}

		if (!_integrationRegistry.IsEnabled(integrationId))
		{
			_logger.Debug("Skipping action {IntegrationId}.{ActionId}: integration disabled", integrationId, actionId);
			run.Record(block.Id,
				block.Label,
				integrationId,
				actionId,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.IntegrationDisabled,
				AppStrings.Errors.Actions.StepIntegrationDisabled(),
				0);
			return;
		}

		var action = _integrationRegistry.FindAction(integrationId, actionId);
		if (action is null)
		{
			_logger.Warning("Skipping unknown action {IntegrationId}.{ActionId}", integrationId, actionId);
			run.Record(block.Id,
				block.Label,
				integrationId,
				actionId,
				ActionOutcomeStatus.Failed,
				ActionExecutionErrorCodes.ActionNotFound,
				AppStrings.Errors.Actions.StepActionGone(),
				0);
			return;
		}

		var parameters = await BuildParameterDictionary(block.Parameters, run.Variables);
		var stopwatch = Stopwatch.StartNew();
		WidgetOptimisticStateIdentity? optimisticIdentity = null;
		long optimisticGeneration = 0;
		if (run.OwnerWidgetId is { } widgetId &&
			action is IStateProviderActionDefinition &&
			_optimisticStates is not null)
		{
			optimisticIdentity = new WidgetOptimisticStateIdentity(widgetId, block.Id, integrationId, actionId);
			optimisticGeneration = _optimisticStates.Begin(optimisticIdentity);
		}

		try
		{
			var executor = action.CreateExecutor();
			var result = await executor.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters,
				OriginClientId = run.OriginClientId,
				OwnerWidgetId = run.OwnerWidgetId?.ToString(),
				Interactions = _interactions,
				Ui = _uiInteractions.ForIntegration(integrationId),
				CancellationToken = cancellationToken,
				CallDepth = ScriptCallDepth.Current
			});
			stopwatch.Stop();

			if (result.Status == ActionResultStatus.Failed)
			{
				run.Record(block.Id,
					block.Label,
					integrationId,
					actionId,
					ActionOutcomeStatus.Failed,
					result.ErrorCode,
					ActionErrorSanitizer.ClampMessage(result.ErrorMessage),
					stopwatch.ElapsedMilliseconds);
			}
			else
			{
				var status = result.Status == ActionResultStatus.Accepted
					? ActionOutcomeStatus.Accepted
					: ActionOutcomeStatus.Succeeded;
				run.Record(block.Id,
					block.Label,
					integrationId,
					actionId,
					status,
					null,
					null,
					stopwatch.ElapsedMilliseconds);

				_musicPlayerPollNudge.NoteActionExecuted(integrationId);

				if (optimisticIdentity is not null &&
					result.ExpectedStateId is { Length: > 0 } expectedStateId &&
					_optimisticStates!.TryApply(optimisticIdentity, optimisticGeneration, expectedStateId))
				{
					_stateEvalQueue?.Enqueue(optimisticIdentity.WidgetId);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			var (code, message) = ActionErrorSanitizer.Sanitize(ex);
			_logger.Error(ex,
				"Action {IntegrationId}.{ActionId} failed (execution {ExecutionId}, block {BlockId})",
				integrationId,
				actionId,
				run.ExecutionId,
				block.Id);
			run.Record(block.Id,
				block.Label,
				integrationId,
				actionId,
				ActionOutcomeStatus.Failed,
				code,
				message,
				stopwatch.ElapsedMilliseconds);

			// The notification carries one composed sentence, so the name cannot travel as a reference and
			// has to be resolved here - interpolating the LocalizedText itself would ship a raw key.
			var actionName = await ResolveActionName(action);

			var notifyCulture = (await _preferences.GetLocalization()).Culture;
			_userNotificationStore.Raise(new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Error,
				Kind = UserNotificationKind.Error,
				Title = _localization.Resolve(AppStrings.Notifications.ActionFailed(name: actionName), notifyCulture) ??
					$"\"{actionName}\" failed",
				Message = _localization.Resolve(message, notifyCulture),
				Action = new UserNotificationAction(UserNotificationActionKind.OpenLogs, null),
				DedupeKey = $"action-failed:{integrationId}.{actionId}"
			});
		}
	}

	private async Task<string> ResolveActionName(IActionDefinition action)
	{
		var settings = await _preferences.GetLocalization();
		return _localization.Resolve(action.Name, settings.Culture) ?? action.Id;
	}

	private async Task ExecuteDelayAsync(
		ActionBlock block,
		VariableContext variables,
		CancellationToken cancellationToken)
	{
		var duration = await GetNumberParameter(block, "duration", variables) ?? 0;
		var milliseconds = Math.Clamp((int)Math.Round(duration), 0, 60_000);
		if (milliseconds > 0)
		{
			await Task.Delay(milliseconds, cancellationToken);
		}
	}

	private async Task ExecuteConditionAsync(
		ActionBlock block,
		FlowRun run,
		CancellationToken cancellationToken)
	{
		if (block.Branches is { Count: > 0 } branches)
		{
			foreach (var branch in branches)
			{
				var kind = branch.Kind.ToLowerInvariant();
				if (kind == "else" || EvaluateCondition(branch.Condition, run.Variables))
				{
					await ExecuteBlocksAsync(branch.Children, run, cancellationToken);
					return;
				}
			}
		}
	}

	private async Task ExecuteLoopAsync(
		ActionBlock block,
		FlowRun run,
		CancellationToken cancellationToken)
	{
		switch (block.BlockType)
		{
			case "repeatLoop":
				var count = Math.Clamp((int)Math.Round(await GetNumberParameter(block, "count", run.Variables) ?? 0),
					0,
					10_000);
				for (var i = 0; i < count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						await ExecuteBlocksAsync(block.Children, run, cancellationToken);
					}
					catch (BreakLoopException)
					{
						return;
					}
				}

				break;
			case "whileLoop":
				var iterations = 0;
				while (EvaluateCondition(block.Condition, run.Variables) && iterations++ < MaxWhileIterations)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						await ExecuteBlocksAsync(block.Children, run, cancellationToken);
					}
					catch (BreakLoopException)
					{
						return;
					}
				}

				if (iterations >= MaxWhileIterations)
				{
					_logger.Warning("Stopped whileLoop block {BlockId} after {MaxIterations} iterations",
						block.Id,
						MaxWhileIterations);
				}

				break;
			default:
				_logger.Warning("Skipping unsupported loop block {BlockType}", block.BlockType);
				run.Record(block.Id,
					block.Label,
					block.IntegrationId,
					block.ActionId,
					ActionOutcomeStatus.Failed,
					ActionExecutionErrorCodes.UnsupportedBlock,
					AppStrings.Errors.Actions.StepTypeNotSupported(),
					0);
				break;
		}
	}

	private bool EvaluateCondition(JsonElement condition, VariableContext variables)
	{
		if (condition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return false;
		}

		return _conditionEvaluator.EvaluateExpression(condition, variables);
	}

	private async Task<Dictionary<string, object>> BuildParameterDictionary(
		IEnumerable<ActionBlockParameter> parameters,
		VariableContext variables)
	{
		var result = new Dictionary<string, object>();
		foreach (var parameter in parameters)
		{
			if (string.IsNullOrWhiteSpace(parameter.Name))
			{
				continue;
			}

			result[parameter.Name] = await ConvertParameterValue(parameter, variables) ?? string.Empty;
		}

		return result;
	}

	private async Task<object?> GetParameterValue(ActionBlock block, string name, VariableContext variables)
	{
		var parameter
			= block.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		return parameter is null ? null : await ConvertParameterValue(parameter, variables);
	}

	private async Task<double?> GetNumberParameter(ActionBlock block, string name, VariableContext variables)
	{
		var value = await GetParameterValue(block, name, variables);
		return ActionConditionEvaluator.TryGetDouble(value, out var number) ? number : null;
	}

	private async Task<object?> ConvertParameterValue(ActionBlockParameter parameter, VariableContext variables)
	{
		var type = parameter.Type.ToLowerInvariant();

		if (parameter.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return type switch
			{
				"number" or "duration" => 0d,
				"boolean" => false,
				"multiselect" => Array.Empty<string>(),
				"keyvalue" => new Dictionary<string, string>(),
				_ => string.Empty
			};
		}

		return parameter.Value.ValueKind switch
		{
			JsonValueKind.String => ConvertStringParameter(parameter, variables),
			JsonValueKind.Number => parameter.Value.TryGetInt64(out var integer)
				? integer
				: parameter.Value.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Object => await ConvertObjectParameter(parameter, variables),
			JsonValueKind.Array => ConvertArrayParameter(parameter, variables),
			_ => parameter.Value.ToString()
		};
	}

	private async Task<object> ConvertObjectParameter(ActionBlockParameter parameter, VariableContext variables)
	{
		if (TryGetSecretReference(parameter.Value, out var secretId))
		{
			return await _secretService.Resolve(secretId) ?? string.Empty;
		}

		var resolved = ActionConditionEvaluator.ResolveVariableReference(parameter.Value, variables);
		if (resolved is null)
		{
			return parameter.Type.ToLowerInvariant() switch
			{
				"keyvalue" => ConvertKeyValueParameter(parameter.Value, variables),
				"hotkey" or "object" or "json" => ConvertJsonElement(parameter.Value) ?? parameter.Value.GetRawText(),
				"keyboardsequence" =>
					_conditionEvaluator.RenderTemplateString(parameter.Value.GetRawText(), variables) ??
					parameter.Value.GetRawText(),
				_ => parameter.Value.GetRawText()
			};
		}

		return parameter.Type.ToLowerInvariant() switch
		{
			"number" or "duration" => ActionConditionEvaluator.TryGetDouble(resolved, out var n) ? n : 0d,
			"boolean" => resolved switch
			{
				bool b => b,
				string s => bool.TryParse(s, out var b) && b,
				double d => d != 0d,
				long l => l != 0L,
				_ => false
			},
			"multiselect" => resolved is string single ? new[] { single } : resolved,
			_ => resolved
		};
	}

	private object ConvertStringParameter(ActionBlockParameter parameter, VariableContext variables)
	{
		var raw = parameter.Value.GetString() ?? string.Empty;
		var type = parameter.Type.ToLowerInvariant();

		var rendered = RendersLiquid(type)
			? _conditionEvaluator.RenderTemplateString(raw, variables) ?? string.Empty
			: raw;

		return type switch
		{
			"number" when double.TryParse(rendered, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) =>
				number,
			"duration" => ParseDurationMilliseconds(rendered),
			"boolean" when bool.TryParse(rendered, out var boolean) => boolean,
			_ => rendered
		};
	}

	private object ConvertArrayParameter(ActionBlockParameter parameter, VariableContext variables)
	{
		if (parameter.Type.ToLowerInvariant() is "multiselect")
		{
			return parameter.Value.EnumerateArray()
				.Select(element => element.ValueKind switch
				{
					JsonValueKind.String => _conditionEvaluator
							.RenderTemplateString(element.GetString() ?? string.Empty, variables) ??
						string.Empty,
					JsonValueKind.Object => ActionConditionEvaluator
							.ResolveVariableReference(element, variables)?.ToString() ??
						element.GetRawText(),
					_ => element.ToString()
				})
				.ToArray();
		}

		return ConvertJsonElement(parameter.Value) ?? parameter.Value.GetRawText();
	}

	private Dictionary<string, string> ConvertKeyValueParameter(JsonElement element, VariableContext variables)
	{
		var result = new Dictionary<string, string>();
		foreach (var property in element.EnumerateObject())
		{
			var value = property.Value.ValueKind == JsonValueKind.String
				? property.Value.GetString() ?? string.Empty
				: property.Value.ToString();
			result[property.Name] = _conditionEvaluator.RenderTemplateString(value, variables) ?? value;
		}

		return result;
	}

	private static object? ConvertJsonElement(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
			JsonValueKind.Object => element.EnumerateObject()
				.ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
			_ => element.GetRawText()
		};
	}

	private static bool RendersLiquid(string? type)
	{
		return type is null
			or ""
			or "string"
			or "number"
			or "boolean"
			or "duration"
			or "autocomplete"
			or "file"
			or "folder"
			or "color"
			or "url"
			or "ipaddress"
			or "datetime"
			or "json"
			or "icon"
			or "image";
	}

	private static double ParseDurationMilliseconds(string value)
	{
		var trimmed = value.Trim();
		if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
		{
			return plain;
		}

		var (factor, suffixLength) = trimmed switch
		{
			_ when trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => (1d, 2),
			_ when trimmed.EndsWith('s') || trimmed.EndsWith('S') => (1_000d, 1),
			_ when trimmed.EndsWith('m') || trimmed.EndsWith('M') => (60_000d, 1),
			_ when trimmed.EndsWith('h') || trimmed.EndsWith('H') => (3_600_000d, 1),
			_ => (0d, 0)
		};

		if (suffixLength > 0 &&
			double.TryParse(trimmed[..^suffixLength], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
		{
			return number * factor;
		}

		return 0d;
	}

	private static bool TryGetSecretReference(JsonElement element, out Guid secretId)
	{
		secretId = Guid.Empty;

		return element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty("$secret", out var reference) &&
			reference.ValueKind == JsonValueKind.String &&
			Guid.TryParse(reference.GetString(), out secretId);
	}

	private static bool TryGetDouble(object? value, out double number)
		=> ActionConditionEvaluator.TryGetDouble(value, out number);

	private static (string? IntegrationId, string? ActionId) ResolveActionIdentity(ActionBlock block)
	{
		if (!string.IsNullOrWhiteSpace(block.IntegrationId) && !string.IsNullOrWhiteSpace(block.ActionId))
		{
			return (block.IntegrationId, block.ActionId);
		}

		if (string.IsNullOrWhiteSpace(block.BlockType))
		{
			return (null, null);
		}

		var separatorIndex = block.BlockType.IndexOf('.', StringComparison.Ordinal);
		if (separatorIndex <= 0 || separatorIndex >= block.BlockType.Length - 1)
		{
			return (null, null);
		}

		return (block.BlockType[..separatorIndex], block.BlockType[(separatorIndex + 1)..]);
	}

	private static bool IsIntegrationActionBlockType(string? blockType) => !string.IsNullOrWhiteSpace(blockType) &&
		blockType.Contains('.', StringComparison.Ordinal);

	private sealed class FlowRun
	{
		public FlowRun(Guid executionId, VariableContext variables, string? originClientId, Guid? ownerWidgetId)
		{
			ExecutionId = executionId;
			Variables = variables;
			OriginClientId = originClientId;
			OwnerWidgetId = ownerWidgetId;
		}

		public Guid ExecutionId { get; }
		public VariableContext Variables { get; }
		public string? OriginClientId { get; }
		public Guid? OwnerWidgetId { get; }
		public List<ActionExecutionOutcome> Outcomes { get; } = [];

		public void Record(
			string blockId,
			string? label,
			string? integrationId,
			string? actionId,
			ActionOutcomeStatus status,
			string? errorCode,
			LocalizedText errorMessage,
			long durationMs)
		{
			Outcomes.Add(new ActionExecutionOutcome(blockId,
				label,
				integrationId,
				actionId,
				status,
				errorCode,
				errorMessage,
				durationMs));
		}
	}

	private sealed class BreakLoopException : Exception;
}
