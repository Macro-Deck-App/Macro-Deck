using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.SystemIntegrations.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Variables;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
public class FlowExecutorVariableFreshnessTests
{
	private const string AddOneToCounter =
		"""
		{ "id": "set", "type": "action", "blockType": "variables.set-variable",
		  "integrationId": "variables", "actionId": "set-variable",
		  "parameters": [
		    { "name": "variable", "type": "autocomplete", "value": "counter" },
		    { "name": "operation", "type": "choice", "value": "add" },
		    { "name": "value", "type": "string", "value": "1" }
		  ] }
		""";

	private List<string> _markers = null!;
	private CapturingActionDefinition _capture = null!;
	private VariableService _service = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public async Task SetUp()
	{
		_markers = [];
		_capture = new CapturingActionDefinition();
		_capture.OnExecuted = () => _markers.Add(_capture.CapturedParameters!["marker"].ToString()!);

		var registry = new VariableRegistry();
		_service = TestVariableServices.Create(registry, new NullUserVariableStore(), new RecordingMediator());
		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => _service)
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [_capture] });
		integrations.Add(new FakeIntegration
		{
			Id = "variables",
			Actions = [new SetVariableActionDefinition(() => _writer)]
		});

		var renderer = new VariableTemplateRenderer(registry);
		_executor = new FlowExecutor(integrations,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(integrations),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Log.Logger);

		await _service.CreateUserVariable("counter", VariableScope.Global, null, VariableType.Numeric, 0, null);
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
	}

	[Test]
	public async Task A_step_after_a_set_variable_step_renders_the_value_that_was_just_set()
	{
		var flows = Flow(AddOneToCounter, Capture("{{ vars.counter }}"));

		await Run(flows);
		await Run(flows);
		await Run(flows);

		Assert.That(_markers, Is.EqualTo(new[] { "1", "2", "3" }));
	}

	[Test]
	public async Task A_delay_between_the_set_and_the_read_does_not_change_what_is_read()
	{
		await Run(Flow(AddOneToCounter,
			"""
			{ "id": "wait", "type": "delay", "blockType": "delay",
			  "parameters": [{ "name": "duration", "type": "duration", "value": 1 }] }
			""",
			Capture("{{ vars.counter }}")));

		Assert.That(_markers, Is.EqualTo(new[] { "1" }));
	}

	[Test]
	public async Task An_if_after_a_set_variable_step_decides_on_the_value_that_was_just_set()
	{
		await Run(Flow(AddOneToCounter,
			$$$"""
			   {
			     "id": "if", "type": "condition", "blockType": "ifElse",
			     "branches": [
			       { "id": "br-if", "kind": "if",
			         "condition": { "kind": "compare", "id": "c1", "left": "{{ vars.counter }}", "operator": "==", "right": 1 },
			         "children": [ {{{Capture("then")}}} ] },
			       { "id": "br-else", "kind": "else", "children": [ {{{Capture("else")}}} ] }
			     ]
			   }
			   """));

		Assert.That(_markers, Is.EqualTo(new[] { "then" }));
	}

	[Test]
	public async Task A_while_loop_stops_once_its_body_has_moved_the_variable_past_the_condition()
	{
		await Run(Flow($$$"""
						  {
						    "id": "while", "type": "loop", "blockType": "whileLoop",
						    "condition": { "kind": "compare", "id": "c1", "left": "{{ vars.counter }}", "operator": "<", "right": 3 },
						    "children": [ {{{AddOneToCounter}}}, {{{Capture("{{ vars.counter }}")}}} ]
						  }
						  """));

		Assert.That(_markers, Is.EqualTo(new[] { "1", "2", "3" }));
	}

	[Test]
	public async Task Event_parameters_and_script_inputs_stay_visible_after_a_set_variable_step()
	{
		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = Flow(AddOneToCounter, Capture("{{ event.who }}/{{ vars.mode }}/{{ vars.counter }}")),
				Trigger = TriggerSelector.ById("t1"),
				EventParameters = new Dictionary<string, object?> { ["who"] = "alice" },
				ScriptInputs = new Dictionary<string, object?> { ["mode"] = "fast" }
			},
			CancellationToken.None);

		Assert.That(_markers, Is.EqualTo(new[] { "alice/fast/1" }));
	}

	private Task<FlowExecutionResult> Run(string flowsSource)
	{
		return _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flowsSource,
				Trigger = TriggerSelector.ById("t1")
			},
			CancellationToken.None);
	}

	private static string Flow(params string[] blocks)
	{
		return $$"""
				 {
				   "flows": [
				     { "triggerId": "t1", "triggerType": "onShortPress", "children": [ {{string.Join(",", blocks)}} ] }
				   ]
				 }
				 """;
	}

	private static string Capture(string marker)
	{
		return $$"""
				 { "id": "{{Guid.NewGuid()}}", "type": "action", "blockType": "integration.capture",
				   "integrationId": "integration", "actionId": "capture",
				   "parameters": [{ "name": "marker", "type": "string", "value": "{{marker}}" }] }
				 """;
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(
			string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(
			string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}
}
