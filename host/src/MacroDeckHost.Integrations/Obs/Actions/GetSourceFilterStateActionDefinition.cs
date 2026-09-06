using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class GetSourceFilterStateActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string SourceParameter = "source";
	internal const string FilterParameter = "filter";
	internal const string VariableParameter = "variable";

	private readonly ObsTargetResolver _resolver;
	private readonly VariableApiAccessor _variables;

	public GetSourceFilterStateActionDefinition(Func<ObsConnection?> resolver, VariableApiAccessor variables)
		: this(ObsTargetResolver.Legacy(resolver), variables)
	{
	}

	public GetSourceFilterStateActionDefinition(ObsTargetResolver resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-source-filter-state";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.GetFilterState.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.GetFilterState.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SourceParameter,
			label: AppStrings.Integrations.Obs.Params.Source(),
			required: true),
		ActionParameter.DynamicChoice(FilterParameter,
			label: AppStrings.Integrations.Obs.Params.Filter(),
			required: true),
		ActionParameter.Text(VariableParameter,
			label: AppStrings.Integrations.Obs.Params.SaveToVariable(),
			description: AppStrings.Integrations.Obs.Actions.GetFilterState.VariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

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
			IntegrationLog.For<GetSourceFilterStateActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;
		private readonly VariableApiAccessor _variables;

		public Executor(ObsTargetResolver resolver, VariableApiAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
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
				_logger.Warning("OBS get-filter action skipped: source or filter not selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSourceOrFilterSelected());
			}

			if (context.Parameters.GetValueOrDefault(VariableParameter) is not string variable ||
				string.IsNullOrWhiteSpace(variable))
			{
				_logger.Warning("OBS get-filter action skipped: no target variable");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoTargetVariable());
			}

			var enabled = await connection.GetSourceFilterEnabledAsync(source, filter);
			if (enabled is null)
			{
				_logger.Warning("OBS get-filter action skipped: state unavailable for '{Source}'/'{Filter}'",
					source,
					filter);
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Obs.Errors.FilterNotFound(filter: filter, source: source));
			}

			var written = await ObsVariableWriter.WriteAsync(_variables, variable, VariableType.Boolean, enabled.Value);

			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Obs.Errors.VariableWriteFailed(variable: variable));
		}
	}
}
