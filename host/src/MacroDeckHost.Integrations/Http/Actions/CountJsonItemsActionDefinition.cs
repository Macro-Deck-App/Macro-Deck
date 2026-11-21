using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class CountJsonItemsActionDefinition : IActionDefinition
{
	private readonly HttpVariableAccessor _variables;

	public CountJsonItemsActionDefinition(HttpVariableAccessor variables)
	{
		_variables = variables;
	}

	public string Id => "count-json-items";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.None;

	public LocalizedText Name => AppStrings.Integrations.Http.Actions.CountJsonItemsName();

	public LocalizedText Description => AppStrings.Integrations.Http.Actions.CountJsonItemsDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		JsonEditActionRunner.JsonInput(),
		JsonEditActionRunner.Path("$"),
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
			if (JsonEditActionRunner.TryReadInput(context.Parameters, true, out var input) is { } inputFailure)
			{
				return Task.FromResult(inputFailure);
			}

			var status = JsonEditor.Count(input.Document, input.Path, out var count);
			return JsonEditActionRunner.SaveAsync(context,
				_variables,
				input.VariableName,
				status,
				VariableType.Numeric,
				() => count);
		}
	}
}
