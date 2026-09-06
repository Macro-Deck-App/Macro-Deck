using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SetSourceFilterActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string SourceParameter = "source";
	internal const string FilterParameter = "filter";
	internal const string ModeParameter = "mode";

	private const string ModeEnable = "enable";
	private const string ModeDisable = "disable";
	private const string ModeToggle = "toggle";

	private readonly ObsTargetResolver _resolver;

	public SetSourceFilterActionDefinition(Func<ObsConnection?> resolver)
		: this(ObsTargetResolver.Legacy(resolver))
	{
	}

	public SetSourceFilterActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-source-filter";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetSourceFilter.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetSourceFilter.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SourceParameter,
			label: AppStrings.Integrations.Obs.Params.Source(),
			required: true),
		ActionParameter.DynamicChoice(FilterParameter,
			label: AppStrings.Integrations.Obs.Params.Filter(),
			required: true),
		ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = ModeToggle, Label = AppStrings.Integrations.Obs.Params.ModeToggle() },
				new ActionParameterOption
					{ Value = ModeEnable, Label = AppStrings.Integrations.Obs.Actions.SetSourceFilter.ModeEnable() },
				new ActionParameterOption
					{ Value = ModeDisable, Label = AppStrings.Integrations.Obs.Actions.SetSourceFilter.ModeDisable() }
			],
			label: AppStrings.Integrations.Obs.Params.Mode(),
			defaultValue: ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(SourceParameter)?.ToString() is not { Length: > 0 } source ||
			parameters.GetValueOrDefault(FilterParameter)?.ToString() is not { Length: > 0 } filter)
		{
			return null;
		}

		var connection = _resolver.ForOptions(parameters);
		var enabled = connection is null ? null : await connection.GetSourceFilterEnabledCachedAsync(source, filter);
		return ActionStates.Snapshot(ActionStates.Enablement, enabled);
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
		IReadOnlyList<string> values = [];

		if (connection is not null)
		{
			if (context.ParameterName == FilterParameter &&
				context.CurrentParameters.GetValueOrDefault(SourceParameter) is string source &&
				!string.IsNullOrWhiteSpace(source))
			{
				values = await connection.GetSourceFilterNamesAsync(source);
			}
			else if (context.ParameterName == SourceParameter)
			{
				values = await connection.GetSourceNamesAsync();
			}
		}

		return new DynamicOptionsResult
		{
			Options = values.Select(v => new ActionParameterOption { Value = v, Label = v }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetSourceFilterActionDefinition>(ObsIntegration.IntegrationId);

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

			if (context.Parameters.GetValueOrDefault(SourceParameter) is not string source ||
				string.IsNullOrWhiteSpace(source) ||
				context.Parameters.GetValueOrDefault(FilterParameter) is not string filter ||
				string.IsNullOrWhiteSpace(filter))
			{
				_logger.Warning("OBS filter action skipped: source or filter not selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSourceOrFilterSelected());
			}

			var mode = context.Parameters.GetValueOrDefault(ModeParameter) as string ?? ModeToggle;
			var succeeded = mode switch
			{
				ModeEnable => await connection.SetSourceFilterEnabledAsync(source, filter, true),
				ModeDisable => await connection.SetSourceFilterEnabledAsync(source, filter, false),
				_ => await connection.ToggleSourceFilterAsync(source, filter)
			};

			return succeeded
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}
	}
}
