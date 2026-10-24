using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SetInputMonitoringActionDefinition : IDynamicOptionsActionDefinition,
	IStateProviderActionDefinition
{
	internal const string InputParameter = "input";
	internal const string ModeParameter = "mode";
	internal const string MonitorTypeParameter = "monitorType";

	internal const string ModeToggle = "toggle";
	internal const string ModeOn = "on";
	internal const string ModeOff = "off";

	internal const string MonitorOnly = "monitor-only";
	internal const string MonitorAndOutput = "monitor-and-output";

	private readonly ObsTargetResolver _resolver;

	public SetInputMonitoringActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-input-monitoring";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetInputMonitoring.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetInputMonitoring.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(InputParameter,
			AppStrings.Integrations.Obs.Params.Input(),
			required: true),
		ActionParameter.Choice(ModeParameter,
			[
				new ActionParameterOption
					{ Value = ModeToggle, Label = AppStrings.Integrations.Obs.Params.ModeToggle() },
				new ActionParameterOption
					{ Value = ModeOn, Label = AppStrings.Integrations.Obs.Actions.SetInputMonitoring.ModeOn() },
				new ActionParameterOption
					{ Value = ModeOff, Label = AppStrings.Integrations.Obs.Actions.SetInputMonitoring.ModeOff() }
			],
			AppStrings.Integrations.Obs.Params.Mode(),
			defaultValue: ModeToggle),
		ActionParameter.Choice(MonitorTypeParameter,
				[
					new ActionParameterOption
					{
						Value = MonitorAndOutput,
						Label = AppStrings.Integrations.Obs.Actions.SetInputMonitoring.MonitorAndOutput()
					},
					new ActionParameterOption
					{
						Value = MonitorOnly,
						Label = AppStrings.Integrations.Obs.Actions.SetInputMonitoring.MonitorOnly()
					}
				],
				AppStrings.Integrations.Obs.Params.MonitorType(),
				defaultValue: MonitorAndOutput)
			.OnlyWhen(ModeParameter, ModeToggle, ModeOn)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_resolver);
	}

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(InputParameter)?.ToString() is not { Length: > 0 } input)
		{
			return null;
		}

		var connection = _resolver.ForOptions(parameters);
		var monitorType = connection is null ? null : await connection.GetInputAudioMonitorTypeAsync(input);
		return ActionStates.Snapshot(ActionStates.Monitoring,
			monitorType is null ? null : monitorType != ObsMonitorTypes.None);
	}

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == ObsTargetResolver.ConfigurationParameter)
		{
			return _resolver.ConfigurationOptions();
		}

		var connection = _resolver.ForOptions(context.CurrentParameters);
		var inputs = connection is null ? [] : await connection.GetInputNamesAsync();

		return new DynamicOptionsResult
		{
			Options = inputs.Select(i => new ActionParameterOption { Value = i, Label = i }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetInputMonitoringActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;

		public Executor(ObsTargetResolver resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			if (context.Parameters.GetValueOrDefault(InputParameter) is not string input
				|| string.IsNullOrWhiteSpace(input))
			{
				_logger.Warning("OBS monitoring action skipped: no input selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoInputSelected());
			}

			var activeType = context.Parameters.GetValueOrDefault(MonitorTypeParameter) as string == MonitorOnly
				? ObsMonitorTypes.MonitorOnly
				: ObsMonitorTypes.MonitorAndOutput;
			var result = (context.Parameters.GetValueOrDefault(ModeParameter) as string) switch
			{
				ModeOn => await connection.SetInputAudioMonitorTypeAsync(input, activeType),
				ModeOff => await connection.SetInputAudioMonitorTypeAsync(input, ObsMonitorTypes.None),
				_ => await connection.ToggleInputAudioMonitorTypeAsync(input, activeType)
			};

			return ObsCommandResults.ToActionResult(result);
		}
	}
}
