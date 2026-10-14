using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class VariableRefreshAndBindingWireTests
{
	[Test]
	public void A_definition_refresh_carries_only_its_ids()
	{
		var named = JsonSerializer.Serialize(new VariablesRefreshArguments { DefinitionIds = ["muted"] },
			PluginProtocolJson.Options);
		var all = JsonSerializer.Serialize(new VariablesRefreshArguments(), PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(named, Is.EqualTo("""{"definitionIds":["muted"]}"""));
			Assert.That(all, Is.EqualTo("{}"));
		});
	}

	[Test]
	public void A_bind_request_and_its_answer_keep_their_shape()
	{
		var request = JsonSerializer.Serialize(new VariablesBindArguments
			{
				Bindings =
				[
					new VariableBindingRequestDto
					{
						LocalResourceId = "entity/light.kitchen/state",
						Name = "ha_light_kitchen",
						Type = "Numeric",
						DecimalPlaces = 1,
						MigratedFrom = "v1"
					}
				]
			},
			PluginProtocolJson.Options);
		var answer = JsonSerializer.Serialize(new VariablesBindResult
				{ Outcomes = [VariableBindingOutcomes.Bound, VariableBindingOutcomes.AlreadyBound] },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(request,
				Is.EqualTo(
					"""{"bindings":[{"localResourceId":"entity/light.kitchen/state","name":"ha_light_kitchen","type":"Numeric","decimalPlaces":1,"migratedFrom":"v1"}]}"""));
			Assert.That(answer, Is.EqualTo("""{"outcomes":["bound","already-bound"]}"""));
		});
	}
}
