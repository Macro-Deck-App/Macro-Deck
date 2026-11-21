using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventBindingTrackerVariableReferenceTests
{
	private const string Provider = "com.obs";

	private VariableRegistry _variables = null!;
	private VariableChangeNotifier _notifier = null!;
	private ServiceProvider _provider = null!;
	private EventSubscriptionIndex _index = null!;
	private EventBindingTracker _tracker = null!;
	private List<IReadOnlyList<EventBindingDto>> _delivered = null!;

	[SetUp]
	public void SetUp()
	{
		_variables = new VariableRegistry();
		_notifier = new VariableChangeNotifier();
		var renderer = new VariableTemplateRenderer(_variables);
		_provider = new ServiceCollection()
			.AddScoped<IVariableTemplateRenderer>(_ => renderer)
			.AddScoped<IActionConditionEvaluator>(_ => new ActionConditionEvaluator(renderer))
			.AddSingleton<IEventSubscriptionIndex>(_ => _index)
			.AddSingleton<IVariableChangeNotifier>(_notifier)
			.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None)
			.AddSingleton<IEventBindingTracker, EventBindingTracker>()
			.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		_index = new EventSubscriptionIndex(new StubFolderCache(), new StubAutomationCache());
		_tracker = (EventBindingTracker)_provider.GetRequiredService<IEventBindingTracker>();
		_delivered = [];
		_tracker.Subscribe((providerId, bindings) =>
		{
			if (providerId == Provider)
			{
				_delivered.Add(bindings);
			}

			return Task.CompletedTask;
		});
	}

	[TearDown]
	public async Task TearDown()
	{
		await _provider.DisposeAsync();
	}

	[Test]
	public async Task A_bound_variable_reaches_the_provider_as_the_value_it_holds_for_the_trigger_owner()
	{
		var automation = Guid.NewGuid();
		Seed("scene", "Global scene", VariableScope.Global, null);
		Seed("scene", "Intro", VariableScope.Automation, automation.ToString());

		_index.ReindexAutomation(automation, Flows(Bound("scene")), true);
		await _tracker.FlushAsync();

		var value = _delivered[^1].Single().Parameters["scene"];
		Assert.Multiple(() =>
		{
			Assert.That(value.Value!.Value.GetProperty("$var").GetString(), Is.EqualTo("scene"));
			Assert.That(value.ResolvedValue!.Value.GetString(), Is.EqualTo("Intro"));
		});
	}

	[Test]
	public async Task A_numeric_variable_arrives_as_the_text_the_trigger_compares()
	{
		Seed("threshold", "12.5", VariableScope.Global, null, VariableType.Numeric);

		_index.ReindexAutomation(Guid.NewGuid(), Flows(Bound("threshold", "scene")), true);
		await _tracker.FlushAsync();

		var resolved = _delivered[^1].Single().Parameters["scene"].ResolvedValue!.Value;
		Assert.Multiple(() =>
		{
			Assert.That(resolved.ValueKind, Is.EqualTo(JsonValueKind.String));
			Assert.That(resolved.GetString(), Is.EqualTo("12.5"));
		});
	}

	[Test]
	public async Task A_literal_or_a_missing_variable_carries_no_resolved_value()
	{
		_index.ReindexAutomation(Guid.NewGuid(),
			Flows(new { name = "scene", value = "Intro", @operator = "==" }, Bound("missing", "input")),
			true);
		await _tracker.FlushAsync();

		var parameters = _delivered[^1].Single().Parameters;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["scene"].ResolvedValue, Is.Null);
			Assert.That(parameters["input"].ResolvedValue, Is.Null);
		});
	}

	[Test]
	public async Task Changing_a_bound_variable_redelivers_with_its_new_value()
	{
		var variable = Seed("scene", "Intro", VariableScope.Global, null);
		_index.ReindexAutomation(Guid.NewGuid(), Flows(Bound("scene")), true);
		await _tracker.FlushAsync();

		variable.Value = "Outro";
		_variables.Upsert(variable);
		_notifier.Publish("scene");
		await _tracker.FlushAsync();

		Assert.That(_delivered[^1].Single().Parameters["scene"].ResolvedValue!.Value.GetString(), Is.EqualTo("Outro"));
	}

	[Test]
	public async Task A_burst_of_changes_ends_on_the_last_value()
	{
		var variable = Seed("scene", "Intro", VariableScope.Global, null);
		_index.ReindexAutomation(Guid.NewGuid(), Flows(Bound("scene")), true);
		await _tracker.FlushAsync();

		foreach (var next in new[] { "One", "Two", "Three" })
		{
			variable.Value = next;
			_variables.Upsert(variable);
			_notifier.Publish("scene");
		}

		await _tracker.FlushAsync();

		Assert.That(_tracker.BindingsFor(Provider).Single().Parameters["scene"].ResolvedValue!.Value.GetString(),
			Is.EqualTo("Three"));
	}

	[Test]
	public async Task Changing_an_unbound_variable_delivers_nothing()
	{
		Seed("scene", "Intro", VariableScope.Global, null);
		var other = Seed("cpu", "10", VariableScope.Global, null);
		_index.ReindexAutomation(Guid.NewGuid(), Flows(Bound("scene")), true);
		await _tracker.FlushAsync();
		var deliveries = _delivered.Count;

		other.Value = "90";
		_variables.Upsert(other);
		_notifier.Publish("cpu");
		await _tracker.FlushAsync();

		Assert.That(_delivered, Has.Count.EqualTo(deliveries));
	}

	private VariableEntity Seed(
		string name,
		string value,
		VariableScope scope,
		string? scopeRefId,
		VariableType type = VariableType.Text)
	{
		var variable = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
		_variables.Upsert(variable);
		return variable;
	}

	private static object Bound(string variable, string parameter = "scene")
	{
		return new
		{
			name = parameter, value = new Dictionary<string, string> { ["$var"] = variable }, @operator = "=="
		};
	}

	private static string Flows(params object[] parameters)
	{
		return EventBindingTrackerTests.Flows(Provider, "scene-changed", parameters);
	}
}
