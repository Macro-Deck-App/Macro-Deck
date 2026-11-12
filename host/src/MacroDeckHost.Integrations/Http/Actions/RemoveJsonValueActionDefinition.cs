using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class RemoveJsonValueActionDefinition : IActionDefinition
{
	private readonly HttpVariableAccessor _variables;

	public RemoveJsonValueActionDefinition(HttpVariableAccessor variables)
	{
		_variables = variables;
	}

	public string Id => "remove-json-value";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.None;

	public LocalizedText Name => AppStrings.Integrations.Http.Actions.RemoveJsonValueName();

	public LocalizedText Description => AppStrings.Integrations.Http.Actions.RemoveJsonValueDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		JsonEditActionRunner.JsonInput(),
		JsonEditActionRunner.Path(),
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

			var status = JsonEditor.Remove(input.Document, input.Path);
			return JsonEditActionRunner.SaveAsync(context,
				_variables,
				input.VariableName,
				status,
				VariableType.Text,
				() => JsonEditor.Serialize(input.Document));
		}
	}
}
