using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Actions;

[TestFixture]
public class ArrayTemplatedItemsTests
{
	private const string Items = """["--user={{ vars.user }}", { "$var": "user" }, 5]""";

	private CapturingActionDefinition _plain = null!;
	private CapturingActionDefinition _templated = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_plain = new CapturingActionDefinition
		{
			Id = "plain",
			Parameters = [ActionParameter.Array("arguments", ActionParameter.Text("argument"))]
		};
		_templated = new CapturingActionDefinition
		{
			Id = "templated",
			Parameters = [ActionParameter.Array("arguments", ActionParameter.Text("argument")).WithTemplatedItems()]
		};

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [_plain, _templated] });

		var variables = new VariableRegistry();
		variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "user",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Ada"
		});

		var renderer = new VariableTemplateRenderer(variables);
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
	}

	[Test]
	public async Task An_array_that_opted_in_receives_rendered_string_items_and_resolved_variable_items()
	{
		await Run("templated");

		var items = (IEnumerable<object?>)_templated.CapturedParameters!["arguments"];
		Assert.That(items, Is.EqualTo(new object?[] { "--user=Ada", "Ada", 5L }));
	}

	[Test]
	public async Task An_array_that_did_not_opt_in_receives_its_items_exactly_as_authored()
	{
		await Run("plain");

		var items = ((IEnumerable<object?>)_plain.CapturedParameters!["arguments"]).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.EqualTo(3));
			Assert.That(items[0], Is.EqualTo("--user={{ vars.user }}"));
			Assert.That(items[1], Is.EqualTo(new Dictionary<string, object?> { ["$var"] = "user" }));
			Assert.That(items[2], Is.EqualTo(5L));
		});
	}

	private async Task Run(string actionId)
	{
		var flows = $$"""
					  { "flows": [
					    {
					      "triggerId": "t1", "triggerType": "onShortPress",
					      "children": [
					        { "id": "b1", "type": "action", "blockType": "integration.{{actionId}}",
					          "integrationId": "integration", "actionId": "{{actionId}}",
					          "parameters": [{ "name": "arguments", "type": "array", "value": {{Items}} }] }
					      ]
					    }
					  ] }
					  """;

		var result = await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flows,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Global
			},
			CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
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
