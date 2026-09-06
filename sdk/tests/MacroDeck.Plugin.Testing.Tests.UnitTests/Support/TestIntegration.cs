using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

/// <summary>
/// A minimal, configurable <see cref="IPluginIntegration" /> for tests that need a plugin's shape without a
/// bespoke class per scenario - declares whatever actions, events and variables a test adds through the
/// fluent <c>With*</c> methods, and captures its own <see cref="Context" /> so a <see cref="DelegateAction" />
/// closure can reach it once <see cref="InitializeAsync" /> has run.
///
/// <para>
/// Not sealed: <c>RegisterIntegration</c> keys its singleton registration by the concrete type
/// parameter, so a test that needs two distinct integrations in the same plugin (see
/// <see cref="SecondTestIntegration" />) cannot register this type twice and get two instances.
/// </para>
/// </summary>
internal class TestIntegration : IPluginIntegration, IEventProvider, IVariableProvider
{
	private readonly string _id;
	private readonly List<IActionDefinition> _actions = [];
	private readonly List<EventDefinition> _events = [];
	private readonly List<VariableDefinition> _variables = [];
	private readonly Dictionary<string, Func<object?>> _variableValues = new(StringComparer.Ordinal);

	public TestIntegration(string id = "test.harness.integration") => _id = id;

	/// <summary>How many times <see cref="InitializeAsync" /> has run - a resumed reconnect must not add to this.</summary>
	public int InitializeCount { get; private set; }

	/// <summary>The context this integration was last initialized with, for a <see cref="DelegateAction" /> to reach.</summary>
	public IIntegrationContext? Context { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions => _actions;

	public TestIntegration WithAction(IActionDefinition action)
	{
		_actions.Add(action);
		return this;
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		Context = context;
		InitializeCount++;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	// ----- IEventProvider -----

	public string ProviderName => _id;

	public IReadOnlyList<EventDefinition> EventDefinitions => _events;

	public TestIntegration WithEvent(EventDefinition definition)
	{
		_events.Add(definition);
		return this;
	}

	// ----- IVariableProvider -----

	public IReadOnlyList<VariableDefinition> Variables => _variables;

	public TestIntegration WithVariable(VariableDefinition variable, Func<object?> value)
	{
		_variables.Add(variable);
		_variableValues[variable.ResolvedId!] = value;
		return this;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(_variableValues.TryGetValue(localId, out var value)
			? VariableReading.Of(value())
			: VariableReading.Unavailable);
}

/// <summary>
/// A second, distinct <see cref="TestIntegration" /> type, so a test needing two independent
/// integrations registered in the same plugin (e.g. two declaring the same action id, to prove
/// <c>ProblemsOf</c> catches the collision) can register both without one's DI singleton shadowing the
/// other's.
/// </summary>
internal sealed class SecondTestIntegration(string id = "test.harness.integration.two") : TestIntegration(id);
