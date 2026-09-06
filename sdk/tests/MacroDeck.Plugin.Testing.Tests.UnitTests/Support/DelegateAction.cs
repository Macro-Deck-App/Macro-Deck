using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

/// <summary>An <see cref="IActionDefinition" /> whose behaviour is a plain delegate, so a test can
/// declare exactly the action a scenario needs without a bespoke class per scenario.</summary>
internal sealed class DelegateAction(
	string id,
	Func<ActionExecutionContext, Task<ActionResult>> execute,
	IReadOnlyList<ActionParameter>? parameters = null) : IActionDefinition
{
	public string Id { get; } = id;

	public LocalizedText Name => Id;

	public LocalizedText Description => Id;

	public IReadOnlyList<ActionParameter> Parameters { get; } = parameters ?? [];

	public IActionExecutor CreateExecutor() => new Executor(execute);

	private sealed class Executor(Func<ActionExecutionContext, Task<ActionResult>> execute) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => execute(context);
	}
}
