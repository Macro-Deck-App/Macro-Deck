using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class TestIntegration(params IActionDefinition[] actions) : IPluginIntegration
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = actions;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;
}

internal class TestAction(string id, Func<ActionExecutionContext, Task<ActionResult>>? execute = null)
	: IActionDefinition
{
	public string Id { get; } = id;

	public virtual LocalizedText Name => Id;

	public LocalizedText Description => string.Empty;

	public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

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

internal sealed class TestDynamicOptionsAction(string id) : TestAction(id), IDynamicOptionsActionDefinition
{
	public DynamicOptionsResult ResultToReturn { get; set; } = new() { Options = [] };

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(ResultToReturn);
}

internal sealed class TestStateProviderAction(string id) : TestAction(id), IStateProviderActionDefinition
{
	public ActionStateSnapshot? SnapshotToReturn { get; set; } =
		new(new[] { new ActionStateDefinition("off", "Off"), new ActionStateDefinition("on", "On") }, "on");

	public Task<ActionStateSnapshot?> GetActionStateAsync(IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(SnapshotToReturn);
}

internal sealed class TestIconProviderAction(string id) : TestAction(id), IIconProviderActionDefinition
{
	public ActionIconSnapshot? SnapshotToReturn { get; set; } = new() { Version = "etag-1" };

	public ActionIconContent? ContentToReturn { get; set; } = new([1, 2, 3], "image/png");

	public int ContentCallCount { get; private set; }

	public Task<ActionIconSnapshot?> GetActionIconAsync(IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(SnapshotToReturn);

	public Task<ActionIconContent?> GetActionIconContentAsync(IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
	{
		ContentCallCount++;
		return Task.FromResult(ContentToReturn);
	}
}
