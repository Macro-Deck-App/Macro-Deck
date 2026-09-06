using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Enums;
using Serilog;

namespace MacroDeckHost.Application.Scripts;

public sealed class ScriptRunner : IScriptRunner
{
	private const int MaxCallDepth = 10;

	private readonly IScriptCache _scriptCache;
	private readonly IFlowExecutor _flowExecutor;
	private readonly IWidgetAppearanceService _widgetAppearance;
	private readonly ILogger _logger;

	public ScriptRunner(
		IScriptCache scriptCache,
		IFlowExecutor flowExecutor,
		IWidgetAppearanceService widgetAppearance,
		ILogger logger)
	{
		_scriptCache = scriptCache;
		_flowExecutor = flowExecutor;
		_widgetAppearance = widgetAppearance;
		_logger = logger.ForContext<ScriptRunner>();
	}

	public async Task<FlowExecutionResult> RunAsync(
		Guid scriptId,
		string? originClientId,
		CancellationToken cancellationToken,
		int inheritedDepth = 0,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? ownerWidgetId = null)
	{
		var script = _scriptCache.GetById(scriptId);
		if (script is null)
		{
			_logger.Warning("Script {ScriptId} not found; the action referencing it does nothing", scriptId);
			return Failed(ActionExecutionErrorCodes.ScriptNotFound, "The script no longer exists.");
		}

		// Take the larger of the locally tracked depth and whatever depth the caller reports inheriting,
		// so a local call chain cannot be laundered back to zero by bouncing through a remote host and
		// picking up a fresh budget there.
		var depth = Math.Max(ScriptCallDepth.Current, inheritedDepth);
		if (depth >= MaxCallDepth)
		{
			_logger.Error("Script {ScriptName} ({ScriptId}) exceeded the maximum call depth of {MaxDepth}; " +
				"check for scripts running each other in a cycle",
				script.Name,
				script.Id,
				MaxCallDepth);
			return Failed(ActionExecutionErrorCodes.ScriptDepthExceeded,
				"Scripts are calling each other too deeply - check for a cycle.");
		}

		Guid? resolvedOwnerWidgetId = null;
		if (script.RunsOnWidget)
		{
			resolvedOwnerWidgetId = ResolveOwnerWidget(ownerWidgetId);
			if (resolvedOwnerWidgetId is null)
			{
				_logger.Warning("Script {ScriptName} ({ScriptId}) was not run: no widget to run it on",
					script.Name,
					script.Id);
				return Failed(ActionExecutionErrorCodes.ScriptWidgetRequired,
					"This script runs on a widget, but no widget was supplied to run it on.");
			}
		}

		var binding = ScriptInputBinder.Bind(script.Inputs, inputs);
		if (!binding.Success)
		{
			_logger.Warning("Script {ScriptName} ({ScriptId}) was not run: {Error}",
				script.Name,
				script.Id,
				binding.ErrorMessage);
			return Failed(binding.ErrorCode!, binding.ErrorMessage!);
		}

		ScriptCallDepth.Set(depth + 1);

		_logger.Debug("Running script {ScriptName} ({ScriptId}) at depth {Depth}",
			script.Name,
			script.Id,
			depth + 1);

		var previousInputScope = ScriptInputScope.Current;
		ScriptInputScope.Set(script.Inputs.Select(input => input.Name).ToHashSet(StringComparer.Ordinal));
		try
		{
			var result = await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
				{
					FlowsSource = ScriptFlows.ToFlowsSource(script.Flows),
					Trigger = TriggerSelector.ByType(ScriptFlows.TriggerType),
					Scope = VariableScope.Global,
					OriginClientId = originClientId,
					OwnerWidgetId = resolvedOwnerWidgetId,
					Origin = ExecutionOrigin.Host,
					ScriptInputs = binding.Values
				},
				cancellationToken);

			return WithAppliedInputs(result, binding.AppliedInputs.ToList());
		}
		finally
		{
			ScriptInputScope.Set(previousInputScope);
		}
	}

	// A blank value, the literal "$self" (which only means something at the Run Script action's own
	// call site, resolved before this is ever reached) and an id that no longer resolves to a widget are
	// all treated alike: there is no widget to run on.
	private Guid? ResolveOwnerWidget(string? ownerWidgetId)
	{
		var trimmed = ownerWidgetId?.Trim();
		if (string.IsNullOrEmpty(trimmed) || WidgetTargets.IsSelf(trimmed) || !Guid.TryParse(trimmed, out var id))
		{
			return null;
		}

		return _widgetAppearance.Exists(trimmed) ? id : null;
	}

	private static FlowExecutionResult WithAppliedInputs(FlowExecutionResult result, IReadOnlyList<string> applied)
		=> new()
		{
			ExecutionId = result.ExecutionId,
			Status = result.Status,
			DurationMs = result.DurationMs,
			MatchedFlows = result.MatchedFlows,
			Actions = result.Actions,
			AppliedInputs = applied,
			ErrorCode = result.ErrorCode,
			ErrorMessage = result.ErrorMessage
		};

	private static FlowExecutionResult Failed(string errorCode, string errorMessage) => new()
	{
		ExecutionId = Guid.NewGuid(),
		Status = FlowExecutionStatus.Failed,
		ErrorCode = errorCode,
		ErrorMessage = errorMessage
	};
}
