using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class UserVariablesInvokeArgumentsTests
{
	/// <summary>
	/// The variable type travels as its enumerant name. Declaring it as the enum itself would serialize it
	/// as an integer and freeze the SDK enum's declaration order into the wire contract.
	/// </summary>
	[Test]
	public void A_create_carries_the_variable_type_as_a_string()
	{
		var arguments = new UserVariablesCreateArguments
		{
			Name = "current_track",
			OwnerWidgetId = "11111111-1111-1111-1111-111111111111",
			Type = "Numeric",
			InitialValue = "1",
			DecimalPlaces = 2
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"type\":\"Numeric\""));
			Assert.That(json, Does.Not.Contain("\"type\":1"));
		});

		var round = JsonSerializer.Deserialize<UserVariablesCreateArguments>(json, PluginProtocolJson.Options);

		Assert.That(round, Is.EqualTo(arguments));
	}

	[Test]
	public void A_create_from_a_peer_that_sends_only_the_required_fields_still_reads()
	{
		const string json = """{"name":"current_track","type":"Text"}""";

		var arguments = JsonSerializer.Deserialize<UserVariablesCreateArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments!.Name, Is.EqualTo("current_track"));
			Assert.That(arguments.OwnerWidgetId, Is.Null);
			Assert.That(arguments.InitialValue, Is.Null);
			Assert.That(arguments.DecimalPlaces, Is.Null);
		});
	}
}
