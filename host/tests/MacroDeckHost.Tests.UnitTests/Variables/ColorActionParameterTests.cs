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

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ColorActionParameterTests
{
	private CapturingActionDefinition _opaque = null!;
	private CapturingActionDefinition _translucent = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_opaque = new CapturingActionDefinition
		{
			Id = "opaque",
			Parameters = [ActionParameter.Color("color")]
		};
		_translucent = new CapturingActionDefinition
		{
			Id = "translucent",
			Parameters =
			[
				new ActionParameter { Name = "color", Type = ActionParameterType.Color, AllowAlpha = true }
			]
		};

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [_opaque, _translucent] });

		var variables = new VariableRegistry();
		variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "primary",
			Scope = VariableScope.Global,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = "#3366ffcc"
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

	[TestCase("{{ vars.primary | color }}", "#3366ff")]
	[TestCase("{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}", "#003df5")]
	public async Task An_action_that_did_not_opt_into_alpha_receives_an_opaque_colour(string value, string expected)
	{
		await Run("opaque", value);

		Assert.That(_opaque.CapturedParameters!["color"], Is.EqualTo(expected));
	}

	[Test]
	public async Task An_action_that_allows_alpha_receives_the_translucent_colour()
	{
		await Run("translucent", "{{ vars.primary | color | color_opacity: 50 }}");

		Assert.That(_translucent.CapturedParameters!["color"], Is.EqualTo("#3366ff80"));
	}

	[TestCase("#11223344")]
	[TestCase("#ABCDEF")]
	public async Task A_literal_colour_reaches_the_action_exactly_as_authored(string value)
	{
		await Run("opaque", value);

		Assert.That(_opaque.CapturedParameters!["color"], Is.EqualTo(value));
	}

	[Test]
	public async Task A_reference_to_a_missing_variable_arrives_as_an_empty_value()
	{
		await Run("opaque", "{{ vars.missing | color }}");

		Assert.That(_opaque.CapturedParameters!["color"], Is.EqualTo(string.Empty));
	}

	private async Task Run(string actionId, string value)
	{
		var flows = $$"""
			{ "flows": [
			  {
			    "triggerId": "t1", "triggerType": "onShortPress",
			    "children": [
			      { "id": "b1", "type": "action", "blockType": "integration.{{actionId}}",
			        "integrationId": "integration", "actionId": "{{actionId}}",
			        "parameters": [{ "name": "color", "type": "color", "value": "{{value}}" }] }
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
