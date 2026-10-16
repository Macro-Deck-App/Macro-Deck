using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class GetOutputStateActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string VariableParameter = "variable";

	private readonly ObsTargetResolver _resolver;
	private readonly VariableApiAccessor _variables;

	public GetOutputStateActionDefinition(ObsTargetResolver resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-output-state";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.GetOutputState.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.GetOutputState.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ObsOutputSupport.OutputChoice(),
		ActionParameter.Autocomplete(VariableParameter,
			AppStrings.Integrations.Obs.Params.SaveToVariable(),
			AppStrings.Integrations.Obs.Actions.GetOutputState.VariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables,
			required: true)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_resolver, _variables);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return ObsOutputSupport.OptionsAsync(_resolver, context);
	}

	private sealed class Executor : IActionExecutor
	{
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

			if (ObsOutputSupport.SelectedOutput(context.Parameters) is not { } output)
			{
				return ObsOutputSupport.NoOutputSelected();
			}

			if (context.Parameters.GetValueOrDefault(VariableParameter) is not string variable
				|| string.IsNullOrWhiteSpace(variable))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoTargetVariable());
			}

			var read = await connection.GetOutputActiveAsync(output);
			if (read.Outcome != ObsOutputOutcome.Done)
			{
				return ObsOutputSupport.FromOutcome(read.Outcome, output);
			}

			var written = await ObsVariableWriter.WriteAsync(_variables,
				variable,
				UserVariableContext.From(context),
				VariableType.Boolean,
				read.Active);

			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Obs.Errors.VariableWriteFailed(variable));
		}
	}
}
