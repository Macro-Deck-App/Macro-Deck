using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.OptionsSources;
using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// What the Set Variable action offers to write. The list is writability, not ownership: an
/// integration's volume is as settable as a counter the user made, now that a provider can declare a
/// write capability (issue #820).
/// </summary>
[TestFixture]
internal sealed class WritableVariableOptionsSourceTests
{
	[Test]
	public async Task Offers_every_writable_variable_and_nothing_read_only()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable("counter", VariableClassification.User));
		registry.Upsert(Variable("obs_desktop_audio_volume",
			VariableClassification.Integration,
			write: new VariableWriteCapability()));
		registry.Upsert(Variable("obs_mac_current_scene", VariableClassification.Integration));

		var options = await new UserVariablesOptionsSource(registry).GetOptionsAsync(null, CancellationToken.None);
		var offered = options.Options.Select(o => o.Value).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(offered, Does.Contain("counter"));
			Assert.That(offered,
				Does.Contain("obs_desktop_audio_volume"),
				"a provider variable whose owner accepts a write is writable from a flow too");
			Assert.That(offered,
				Does.Not.Contain("obs_mac_current_scene"),
				"offering one nothing can write configures an action that silently does nothing");
		});
	}

	private static VariableEntity Variable(
		string name,
		VariableClassification classification,
		VariableWriteCapability? write = null)
		=> new()
		{
			Id = Guid.CreateVersion7(),
			Name = name,
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = classification,
			OwnerIntegrationId = classification == VariableClassification.Integration ? "app.macro-deck.obs" : null,
			Write = write,
			Value = string.Empty
		};
}
