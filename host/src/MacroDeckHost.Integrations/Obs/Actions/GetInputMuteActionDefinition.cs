using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class GetInputMuteActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string InputParameter = "input";
	internal const string VariableParameter = "variable";

	private readonly ObsTargetResolver _resolver;
	private readonly VariableApiAccessor _variables;

	public GetInputMuteActionDefinition(Func<ObsConnection?> resolver, VariableApiAccessor variables)
		: this(ObsTargetResolver.Legacy(resolver), variables)
	{
	}

	public GetInputMuteActionDefinition(ObsTargetResolver resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-input-mute";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.GetInputMute.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.GetInputMute.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(InputParameter,
			label: AppStrings.Integrations.Obs.Params.Input(),
			required: true),
		ActionParameter.Text(VariableParameter,
			label: AppStrings.Integrations.Obs.Params.SaveToVariable(),
			description: AppStrings.Integrations.Obs.Actions.GetInputMute.VariableDescription(),
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
			IntegrationLog.For<GetInputMuteActionDefinition>(ObsIntegration.IntegrationId);

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

			if (context.Parameters.GetValueOrDefault(InputParameter) is not string input ||
				string.IsNullOrWhiteSpace(input))
			{
				_logger.Warning("OBS get-mute action skipped: no input selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoInputSelected());
			}

			if (context.Parameters.GetValueOrDefault(VariableParameter) is not string variable ||
				string.IsNullOrWhiteSpace(variable))
			{
				_logger.Warning("OBS get-mute action skipped: no target variable");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoTargetVariable());
			}

			var muted = await connection.GetInputMutedAsync(input);
			if (muted is null)
			{
				_logger.Warning("OBS get-mute action skipped: mute state unavailable for '{Input}'", input);
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Obs.Errors.InputNotFound(input: input));
			}

			var written = await ObsVariableWriter.WriteAsync(_variables, variable, VariableType.Boolean, muted.Value);

			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Obs.Errors.VariableWriteFailed(variable: variable));
		}
	}
}
