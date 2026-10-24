using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class UserVariablesReadWireTests
{
	[Test]
	public void The_read_operation_is_part_of_the_user_variables_vocabulary()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostOperations.UserVariables.Read, Is.EqualTo("read"));
			Assert.That(HostOperations.IsKnown(HostApis.UserVariables, "read"), Is.True);
		});
	}

	[Test]
	public void A_read_request_carries_the_name_and_context()
	{
		var json = JsonSerializer.Serialize(new UserVariablesReadArguments
			{
				Name = "volume",
				OwnerWidgetId = "w1",
				ScriptId = "s1",
				AutomationId = "a1"
			},
			PluginProtocolJson.Options);

		Assert.That(json,
			Is.EqualTo("""{"name":"volume","ownerWidgetId":"w1","scriptId":"s1","automationId":"a1"}"""));
	}

	[Test]
	public void A_read_result_carries_its_status_and_type_as_names_and_its_value_as_json()
	{
		var result = JsonSerializer.Deserialize<UserVariablesReadResult>(
			"""{"status":"Found","type":"Numeric","value":2.5,"text":"2.50"}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo("Found"));
			Assert.That(result.Type, Is.EqualTo("Numeric"));
			Assert.That(result.Value?.GetDouble(), Is.EqualTo(2.5));
			Assert.That(result.Text, Is.EqualTo("2.50"));
		});
	}
}
