using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests;

public class FlowExecutorSwitchTests
{
	private static readonly ILogger _logger = Log.Logger;

	private CapturingActionDefinition _none = null!;
	private CapturingActionDefinition _all = null!;
	private CapturingActionDefinition _one = null!;
	private CapturingActionDefinition _default = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_none = new CapturingActionDefinition { Id = "none" };
		_all = new CapturingActionDefinition { Id = "all" };
		_one = new CapturingActionDefinition { Id = "one" };
		_default = new CapturingActionDefinition { Id = "fallback" };

		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [_none, _all, _one, _default] });

		var variables = new VariableRegistry();
		variables.Upsert(Var("repeat", VariableType.Text, "all"));
		variables.Upsert(Var("level", VariableType.Numeric, "2"));
		variables.Upsert(Var("live", VariableType.Boolean, "true"));
		variables.Upsert(Var("blank", VariableType.Text, string.Empty));

		var renderer = new VariableTemplateRenderer(variables);

		_executor = new FlowExecutor(registry,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(registry),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			_logger);
	}

	private static VariableEntity Var(string name, VariableType type, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value
	};

	private static string Block(string actionId) =>
		$$"""
		  { "id": "b-{{actionId}}", "type": "action", "blockType": "integration.{{actionId}}",
		    "integrationId": "integration", "actionId": "{{actionId}}", "parameters": [] }
		  """;

	private static string Case(string value, string actionId) =>
		$$"""{ "id": "c-{{actionId}}-{{Guid.NewGuid()}}", "kind": "case", "value": {{value}}, "children": [ {{Block(actionId)}} ] }""";

	private static string Default() =>
		$$"""{ "id": "br-else", "kind": "else", "children": [ {{Block("fallback")}} ] }""";

	private static string SwitchFlow(string subject, string type, params string[] branches) =>
		$$"""
		  {
		    "flows": [
		      {
		        "triggerId": "t1", "triggerType": "onShortPress",
		        "children": [
		          {
		            "id": "sw", "type": "{{type}}", "blockType": "switch",
		            "subject": {{subject}},
		            "branches": [ {{string.Join(",", branches)}} ]
		          }
		        ]
		      }
		    ]
		  }
		  """;

	private async Task<FlowExecutionResult> Run(string widgetData)
		=> await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Global
			},
			CancellationToken.None);

	private void AssertOnlyRan(CapturingActionDefinition expected)
		=> Assert.Multiple(() =>
		{
			foreach (var action in new[] { _none, _all, _one, _default })
			{
				Assert.That(action.ExecuteCount, Is.EqualTo(ReferenceEquals(action, expected) ? 1 : 0), action.Id);
			}
		});

	[Test]
	public async Task Repeat_mode_variable_runs_the_case_that_matches_it()
	{
		await Run(SwitchFlow("""{ "$var": "repeat" }""",
			"switch",
			Case("\"none\"", "none"),
			Case("\"all\"", "all"),
			Case("\"one\"", "one")));

		AssertOnlyRan(_all);
	}

	[Test]
	public async Task Number_variable_matches_a_case_typed_as_text()
	{
		await Run(SwitchFlow("""{ "$var": "level" }""", "switch", Case("\"1\"", "none"), Case("\"2\"", "one")));

		AssertOnlyRan(_one);
	}

	[Test]
	public async Task Boolean_variable_matches_a_case_typed_as_text()
	{
		await Run(SwitchFlow("""{ "$var": "live" }""", "switch", Case("\"false\"", "none"), Case("\"true\"", "one")));

		AssertOnlyRan(_one);
	}

	[Test]
	public async Task Literal_subject_is_matched_like_a_variable()
	{
		await Run(SwitchFlow("\"one\"", "switch", Case("\"all\"", "all"), Case("\"one\"", "one")));

		AssertOnlyRan(_one);
	}

	[Test]
	public async Task No_matching_case_runs_the_default()
	{
		await Run(SwitchFlow("""{ "$var": "repeat" }""", "switch", Case("\"none\"", "none"), Case("\"one\"", "one"), Default()));

		AssertOnlyRan(_default);
	}

	[Test]
	public async Task No_matching_case_without_a_default_runs_nothing()
	{
		var result = await Run(SwitchFlow("""{ "$var": "repeat" }""", "switch", Case("\"none\"", "none")));

		Assert.Multiple(() =>
		{
			Assert.That(_none.ExecuteCount, Is.Zero);
			Assert.That(_default.ExecuteCount, Is.Zero);
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
		});
	}

	[Test]
	public async Task First_of_several_matching_cases_runs_and_the_rest_do_not()
	{
		await Run(SwitchFlow("""{ "$var": "repeat" }""", "switch", Case("\"all\"", "all"), Case("\"all\"", "one"), Default()));

		AssertOnlyRan(_all);
	}

	[Test]
	public async Task Matching_is_case_sensitive()
	{
		await Run(SwitchFlow("""{ "$var": "repeat" }""", "switch", Case("\"ALL\"", "all"), Default()));

		AssertOnlyRan(_default);
	}

	[Test]
	public async Task Unresolvable_subject_runs_only_the_default_even_for_an_empty_case()
	{
		await Run(SwitchFlow("""{ "$var": "missing" }""", "switch", Case("\"\"", "none"), Default()));

		AssertOnlyRan(_default);
	}

	[Test]
	public async Task Empty_text_variable_matches_an_empty_case()
	{
		await Run(SwitchFlow("""{ "$var": "blank" }""", "switch", Case("\"\"", "none"), Default()));

		AssertOnlyRan(_none);
	}

	[Test]
	public async Task Disabled_switch_runs_nothing()
	{
		var flow = SwitchFlow("""{ "$var": "repeat" }""", "switch", Case("\"all\"", "all"), Default())
			.Replace("\"id\": \"sw\",", "\"id\": \"sw\", \"disabled\": true,");

		var result = await Run(flow);

		Assert.Multiple(() =>
		{
			Assert.That(_all.ExecuteCount, Is.Zero);
			Assert.That(_default.ExecuteCount, Is.Zero);
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task Unknown_block_type_fails_loudly_instead_of_running_a_branch()
	{
		var result = await Run(SwitchFlow("""{ "$var": "repeat" }""", "switch-from-the-future", Case("\"all\"", "all"), Default()));

		Assert.Multiple(() =>
		{
			Assert.That(result.Actions, Has.Count.EqualTo(1));
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Failed));
			Assert.That(_all.ExecuteCount + _default.ExecuteCount, Is.Zero);
		});
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}
}
