using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsGetActionUserVariableTests
{
	private ActionVariableTargets _targets = null!;
	private VariableApiAccessor _accessor = null!;

	[SetUp]
	public void SetUp()
	{
		_targets = new ActionVariableTargets(ObsIntegration.IntegrationId);
		_accessor = new VariableApiAccessor
		{
			Current = _targets.IntegrationVariables,
			UserVariables = _targets.UserVariables
		};
	}

	[TearDown]
	public void TearDown() => _targets.Dispose();

	[TestCase("test_boolean")]
	[TestCase("vars.test_boolean")]
	public async Task A_variable_the_user_created_receives_the_mute_state(string target)
	{
		await _targets.CreateUserVariable("test_boolean", DomainVariableType.Boolean, false);

		var result = await GetInputMute(muted: true, target);

		var variable = await _targets.Find("test_boolean");
		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf("test_boolean"), Is.EqualTo(true));
			Assert.That(variable!.Classification, Is.EqualTo(VariableClassification.User));
		});
	}

	[Test]
	public async Task A_missing_target_becomes_a_user_variable_of_the_action_type()
	{
		var result = await GetInputMute(muted: true, "mic_muted");

		var variable = await _targets.Find("mic_muted");
		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(variable, Is.Not.Null);
			Assert.That(variable!.Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(variable.Type, Is.EqualTo(DomainVariableType.Boolean));
			Assert.That(await _targets.ValueOf("mic_muted"), Is.EqualTo(true));
		});
	}

	[Test]
	public async Task A_variable_an_earlier_version_created_for_the_action_keeps_updating()
	{
		await _targets.IntegrationVariables.CreateAsync("mic_muted", SdkVariableType.Boolean, false);

		var result = await GetInputMute(muted: true, "mic_muted");

		var variable = await _targets.Find("mic_muted");
		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf("mic_muted"), Is.EqualTo(true));
			Assert.That(variable!.Classification, Is.EqualTo(VariableClassification.Integration));
		});
	}

	[Test]
	public async Task A_user_variable_that_cannot_hold_the_value_fails_the_action_and_keeps_its_value()
	{
		await _targets.CreateUserVariable("counter", DomainVariableType.Numeric, 3);

		var result = await GetInputMute(muted: true, "counter");

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(await _targets.ValueOf("counter"), Is.EqualTo(3m));
		});
	}

	[Test]
	public void Save_to_variable_offers_the_writable_variables()
	{
		var parameter = new GetInputMuteActionDefinition(() => null, _accessor).Parameters
			.Single(p => p.Name == GetInputMuteActionDefinition.VariableParameter);

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.Autocomplete));
			Assert.That(parameter.OptionsSourceId, Is.EqualTo("macrodeck.user-variables"));
		});
	}

	private async Task<ActionResult> GetInputMute(bool muted, string target)
	{
		var client = new FakeObsClient { IsConnected = true };
		client.MutedInputs["Mic"] = muted;
		using var connection = new ObsConnection(client, "ws://localhost:4455", null);
		var action = new GetInputMuteActionDefinition(() => connection, _accessor);

		return await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic",
				[GetInputMuteActionDefinition.VariableParameter] = target
			}
		});
	}
}
