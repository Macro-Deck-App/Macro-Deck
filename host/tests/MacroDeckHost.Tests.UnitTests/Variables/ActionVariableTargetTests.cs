using MacroDeckHost.Integrations.Streamerbot.Actions;
using MacroDeckHost.Integrations.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using Serilog;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class ActionVariableTargetTests
{
	private ActionVariableTargets _targets = null!;

	[SetUp]
	public void SetUp() => _targets = new ActionVariableTargets("app.macro-deck.test");

	[TearDown]
	public void TearDown() => _targets.Dispose();

	[TestCase(0.00001, "0.00001")]
	[TestCase(1e20, "100000000000000000000")]
	public async Task Very_small_and_very_large_numbers_reach_a_number_variable(double reading, string stored)
	{
		await _targets.CreateUserVariable("level", DomainVariableType.Numeric, 0);

		var written = await Write("level", SdkVariableType.Numeric, reading);

		Assert.Multiple(async () =>
		{
			Assert.That(written, Is.True);
			Assert.That((await _targets.Find("level"))!.Value, Is.EqualTo(stored));
		});
	}

	[Test]
	public async Task A_reading_that_is_not_a_number_is_refused()
	{
		await _targets.CreateUserVariable("level", DomainVariableType.Numeric, 1);

		var written = await Write("level", SdkVariableType.Numeric, double.NaN);

		Assert.Multiple(async () =>
		{
			Assert.That(written, Is.False);
			Assert.That(await _targets.ValueOf("level"), Is.EqualTo(1m));
		});
	}

	[Test]
	public async Task A_boolean_written_into_a_text_variable_reads_as_lowercase_true()
	{
		await _targets.CreateUserVariable("state", DomainVariableType.Text, "");

		await Write("state", SdkVariableType.Boolean, true);

		Assert.That(await _targets.ValueOf("state"), Is.EqualTo("true"));
	}

	[Test]
	public void Streamerbot_offers_the_picker_for_the_target_but_not_for_the_variable_it_reads()
	{
		var parameters = new GetGlobalVariableActionDefinition(() => null, new StreamerbotVariableAccessor())
			.Parameters;

		Assert.Multiple(() =>
		{
			Assert.That(parameters.Single(p => p.Name == GetGlobalVariableActionDefinition.VariableParameterName).Type,
				Is.EqualTo(ActionParameterType.String));
			Assert.That(parameters.Single(p => p.Name == GetGlobalVariableActionDefinition.TargetParameterName)
					.OptionsSourceId,
				Is.EqualTo("macrodeck.user-variables"));
		});
	}

	private Task<bool> Write(string name, SdkVariableType type, object value)
		=> ActionVariableTarget.WriteAsync(_targets.UserVariables,
			_targets.IntegrationVariables,
			name,
			null,
			type,
			value,
			new LoggerConfiguration().CreateLogger());
}
