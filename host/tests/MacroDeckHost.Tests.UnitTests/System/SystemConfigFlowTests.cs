using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.System.Configuration;

namespace MacroDeckHost.Tests.UnitTests.System;

[TestFixture]
internal sealed class SystemConfigFlowTests
{
	[Test]
	public async Task The_application_field_suggests_running_processes()
	{
		var result = await new SystemConfigFlow().StartAsync(new FlowContext(), CancellationToken.None);

		var field = result.NextStep!.Fields.Single(candidate => candidate.Name == SystemConfigKeys.Application);
		Assert.Multiple(() =>
		{
			Assert.That(field.Type, Is.EqualTo(ActionParameterType.Autocomplete));
			Assert.That(field.OptionsSourceId, Is.EqualTo("system.processes"));
			Assert.That(field.Required, Is.True);
		});
	}

	[Test]
	public async Task A_named_application_is_stored_trimmed()
	{
		var result = await SubmitAsync(new Dictionary<string, object?>
		{
			[SystemConfigKeys.Name] = " Game ", [SystemConfigKeys.Application] = " game "
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Game"));
			Assert.That(result.Values![SystemConfigKeys.Application], Is.EqualTo(ConfigFlowValue.Plain("game")));
		});
	}

	[Test]
	public async Task A_missing_name_or_application_is_rejected_on_its_field()
	{
		var result = await SubmitAsync(new Dictionary<string, object?>
		{
			[SystemConfigKeys.Name] = "", [SystemConfigKeys.Application] = "  "
		});

		Assert.That(result.FieldErrors!.Keys,
			Is.EquivalentTo(new[] { SystemConfigKeys.Name, SystemConfigKeys.Application }));
	}

	private static Task<ConfigFlowResult> SubmitAsync(IReadOnlyDictionary<string, object?> input)
	{
		return new SystemConfigFlow().SubmitAsync("application", input, new FlowContext(), CancellationToken.None);
	}

	private sealed class FlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}
}
