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

public class FlowExecutorGroupedConditionTests
{
	private static readonly ILogger _logger = Log.Logger;

	private CapturingActionDefinition _thenAction = null!;
	private CapturingActionDefinition _elseAction = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_thenAction = new CapturingActionDefinition { Id = "capture" };
		_elseAction = new CapturingActionDefinition { Id = "capture-2" };

		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration
		{
			Id = "integration",
			Actions = [_thenAction, _elseAction]
		});

		var variables = new VariableRegistry();
		variables.Upsert(Var("streaming", VariableType.Boolean, "true"));
		variables.Upsert(Var("recording", VariableType.Boolean, "false"));
		variables.Upsert(Var("viewers", VariableType.Numeric, "12"));

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

	private const string _thenBlock =
		"""
		{ "id": "then", "type": "action", "blockType": "integration.capture",
		  "integrationId": "integration", "actionId": "capture", "parameters": [] }
		""";

	private const string _elseBlock =
		"""
		{ "id": "else", "type": "action", "blockType": "integration.capture",
		  "integrationId": "integration", "actionId": "capture-2", "parameters": [] }
		""";

	private static string IfElseFlow(string condition) =>
		$$"""
		  {
		    "flows": [
		      {
		        "triggerId": "t1", "triggerType": "onShortPress",
		        "children": [
		          {
		            "id": "if", "type": "condition", "blockType": "ifElse",
		            "branches": [
		              {
		                "id": "br-if", "kind": "if",
		                "condition": {{condition}},
		                "children": [ {{_thenBlock}} ]
		              },
		              { "id": "br-else", "kind": "else", "children": [ {{_elseBlock}} ] }
		            ]
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

	private void AssertThenBranchRan()
		=> Assert.Multiple(() =>
		{
			Assert.That(_thenAction.ExecuteCount, Is.EqualTo(1), "then-branch should have run");
			Assert.That(_elseAction.ExecuteCount, Is.Zero, "else-branch should not have run");
		});

	private void AssertElseBranchRan()
		=> Assert.Multiple(() =>
		{
			Assert.That(_thenAction.ExecuteCount, Is.Zero, "then-branch should not have run");
			Assert.That(_elseAction.ExecuteCount, Is.EqualTo(1), "else-branch should have run");
		});

	[Test]
	public async Task And_group_with_both_sides_true_runs_the_then_branch()
	{
		await Run(IfElseFlow("""
							 {
							   "kind": "and", "id": "g1",
							   "operands": [
							     { "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true },
							     { "kind": "compare", "id": "c2", "left": { "$var": "viewers" }, "operator": ">", "right": 10 }
							   ]
							 }
							 """));

		AssertThenBranchRan();
	}

	[Test]
	public async Task And_group_with_one_side_false_falls_through_to_else()
	{
		await Run(IfElseFlow("""
							 {
							   "kind": "and", "id": "g1",
							   "operands": [
							     { "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true },
							     { "kind": "compare", "id": "c2", "left": { "$var": "recording" }, "operator": "==", "right": true }
							   ]
							 }
							 """));

		AssertElseBranchRan();
	}

	[Test]
	public async Task Or_group_with_one_side_true_runs_the_then_branch()
	{
		await Run(IfElseFlow("""
							 {
							   "kind": "or", "id": "g1",
							   "operands": [
							     { "kind": "compare", "id": "c1", "left": { "$var": "recording" }, "operator": "==", "right": true },
							     { "kind": "compare", "id": "c2", "left": { "$var": "viewers" }, "operator": ">", "right": 10 }
							   ]
							 }
							 """));

		AssertThenBranchRan();
	}

	[Test]
	public async Task Or_group_with_no_side_true_falls_through_to_else()
	{
		await Run(IfElseFlow("""
							 {
							   "kind": "or", "id": "g1",
							   "operands": [
							     { "kind": "compare", "id": "c1", "left": { "$var": "recording" }, "operator": "==", "right": true },
							     { "kind": "compare", "id": "c2", "left": { "$var": "viewers" }, "operator": ">", "right": 100 }
							   ]
							 }
							 """));

		AssertElseBranchRan();
	}

	[Test]
	public async Task Nested_group_is_evaluated_with_its_own_connective()
	{
		await Run(IfElseFlow("""
							 {
							   "kind": "and", "id": "g1",
							   "operands": [
							     { "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true },
							     {
							       "kind": "or", "id": "g2",
							       "operands": [
							         { "kind": "compare", "id": "c2", "left": { "$var": "recording" }, "operator": "==", "right": true },
							         { "kind": "compare", "id": "c3", "left": { "$var": "viewers" }, "operator": "<", "right": 5 }
							       ]
							     }
							   ]
							 }
							 """));

		AssertElseBranchRan();
	}

	[Test]
	public async Task Group_on_an_elseif_branch_decides_that_branch()
	{
		await Run($$"""
					{
					  "flows": [
					    {
					      "triggerId": "t1", "triggerType": "onShortPress",
					      "children": [
					        {
					          "id": "if", "type": "condition", "blockType": "ifElse",
					          "branches": [
					            {
					              "id": "br-if", "kind": "if",
					              "condition": { "kind": "compare", "id": "c0", "left": { "$var": "recording" }, "operator": "==", "right": true },
					              "children": [ {{_elseBlock}} ]
					            },
					            {
					              "id": "br-elseif", "kind": "elseif",
					              "condition": {
					                "kind": "and", "id": "g1",
					                "operands": [
					                  { "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true },
					                  { "kind": "compare", "id": "c2", "left": { "$var": "viewers" }, "operator": ">=", "right": 12 }
					                ]
					              },
					              "children": [ {{_thenBlock}} ]
					            }
					          ]
					        }
					      ]
					    }
					  ]
					}
					""");

		AssertThenBranchRan();
	}

	[Test]
	public async Task While_loop_with_a_group_that_does_not_hold_never_runs_its_body()
	{
		await Run($$"""
					{
					  "flows": [
					    {
					      "triggerId": "t1", "triggerType": "onShortPress",
					      "children": [
					        {
					          "id": "while", "type": "loop", "blockType": "whileLoop",
					          "condition": {
					            "kind": "and", "id": "g1",
					            "operands": [
					              { "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true },
					              { "kind": "compare", "id": "c2", "left": { "$var": "recording" }, "operator": "==", "right": true }
					            ]
					          },
					          "children": [ {{_thenBlock}} ]
					        }
					      ]
					    }
					  ]
					}
					""");

		Assert.That(_thenAction.ExecuteCount, Is.Zero);
	}

	[Test]
	public async Task While_loop_with_a_group_that_holds_runs_its_body()
	{
		await Run($$"""
					{
					  "flows": [
					    {
					      "triggerId": "t1", "triggerType": "onShortPress",
					      "children": [
					        {
					          "id": "while", "type": "loop", "blockType": "whileLoop",
					          "condition": {
					            "kind": "or", "id": "g1",
					            "operands": [
					              { "kind": "compare", "id": "c1", "left": { "$var": "recording" }, "operator": "==", "right": true },
					              { "kind": "compare", "id": "c2", "left": { "$var": "viewers" }, "operator": ">", "right": 10 }
					            ]
					          },
					          "children": [
					            {{_thenBlock}},
					            { "id": "stop", "type": "flow-control", "blockType": "break", "parameters": [] }
					          ]
					        }
					      ]
					    }
					  ]
					}
					""");

		Assert.That(_thenAction.ExecuteCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Single_comparison_leaf_still_decides_the_branch()
	{
		await Run(IfElseFlow(
			"""{ "kind": "compare", "id": "c1", "left": { "$var": "streaming" }, "operator": "==", "right": true }"""));

		AssertThenBranchRan();
	}

	[Test]
	public async Task Single_comparison_leaf_that_does_not_hold_falls_through_to_else()
	{
		await Run(IfElseFlow(
			"""{ "kind": "compare", "id": "c1", "left": { "$var": "recording" }, "operator": "==", "right": true }"""));

		AssertElseBranchRan();
	}

	[Test]
	public async Task Legacy_condition_without_a_kind_still_decides_the_branch()
	{
		await Run(IfElseFlow("""{ "left": { "$var": "streaming" }, "operator": "==", "right": true }"""));

		AssertThenBranchRan();
	}

	[Test]
	public async Task Legacy_condition_without_a_kind_that_does_not_hold_falls_through_to_else()
	{
		await Run(IfElseFlow("""{ "left": { "$var": "viewers" }, "operator": ">", "right": 100 }"""));

		AssertElseBranchRan();
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
