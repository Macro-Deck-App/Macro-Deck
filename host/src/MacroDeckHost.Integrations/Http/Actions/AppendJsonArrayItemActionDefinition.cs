using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class AppendJsonArrayItemActionDefinition : IActionDefinition
{
	private readonly HttpVariableAccessor _variables;

	public AppendJsonArrayItemActionDefinition(HttpVariableAccessor variables)
	{
		_variables = variables;
	}

	public string Id => "append-json-array-item";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.None;

	public LocalizedText Name => AppStrings.Integrations.Http.Actions.AppendJsonArrayItemName();

	public LocalizedText Description => AppStrings.Integrations.Http.Actions.AppendJsonArrayItemDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		JsonEditActionRunner.JsonInput(),
		JsonEditActionRunner.Path(),
		.. JsonEditActionRunner.Value(),
		JsonEditActionRunner.Variable()
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_variables);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly HttpVariableAccessor _variables;

		public Executor(HttpVariableAccessor variables)
		{
			_variables = variables;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (JsonEditActionRunner.TryReadInput(context.Parameters, false, out var input) is { } inputFailure)
			{
				return Task.FromResult(inputFailure);
			}

			if (JsonEditActionRunner.TryReadValue(context.Parameters, out var item) is { } valueFailure)
			{
				return Task.FromResult(valueFailure);
			}

			var status = JsonEditor.Append(input.Document, input.Path, item, out var result);
			return JsonEditActionRunner.SaveAsync(context,
				_variables,
				input.VariableName,
				status,
				VariableType.Text,
				() => JsonEditor.Serialize(result));
		}
	}
}
