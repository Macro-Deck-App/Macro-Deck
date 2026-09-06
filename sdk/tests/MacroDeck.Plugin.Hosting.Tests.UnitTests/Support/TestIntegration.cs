using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>An integration with whatever actions a test needs and nothing else.</summary>
internal class TestIntegration : IPluginIntegration
{
	public TestIntegration(params IActionDefinition[] actions) => Actions = actions;

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsInitialized { get; private set; }

	public IIntegrationContext? Context { get; private set; }

	public int ShutdownCount { get; private set; }

	public Task InitializeAsync(IIntegrationContext context)
	{
		Context = context;
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		ShutdownCount++;
		IsInitialized = false;
		return Task.CompletedTask;
	}
}

/// <summary>An action whose executor does whatever the test hands it.</summary>
internal sealed class TestAction(string id, Func<ActionExecutionContext, Task<ActionResult>>? execute = null)
	: IActionDefinition
{
	public string Id { get; } = id;

	public LocalizedText Name => Id;

	public LocalizedText Description => string.Empty;

	public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

	public MacroDeckPlatform Platforms { get; init; } = MacroDeckPlatform.All;

	/// <summary>The context the last execution was handed, so a test can inspect what was bound.</summary>
	public ActionExecutionContext? LastContext { get; private set; }

	public IActionExecutor CreateExecutor() => new Executor(this, execute);

	private sealed class Executor(TestAction action, Func<ActionExecutionContext, Task<ActionResult>>? execute)
		: IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			action.LastContext = context;
			return execute?.Invoke(context) ?? ActionResult.SucceededTask;
		}
	}
}
