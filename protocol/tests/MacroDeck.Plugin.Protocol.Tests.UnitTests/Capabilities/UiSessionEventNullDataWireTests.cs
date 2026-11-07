using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class UiSessionEventNullDataWireTests
{
	[Test]
	public void An_event_whose_data_is_json_null_reaches_the_plugin_as_a_null_element()
	{
		var arguments = JsonSerializer.Deserialize<UiSessionEventArguments>(
			"""{"sessionId":"s","nodeId":"icons.mouse","name":"change","data":null}""",
			PluginProtocolJson.Options);

		Assert.That(arguments!.Data?.ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	[Test]
	public void An_event_without_data_still_reaches_the_plugin_without_data()
	{
		var arguments = JsonSerializer.Deserialize<UiSessionEventArguments>(
			"""{"sessionId":"s","nodeId":"icons.mouse","name":"press"}""",
			PluginProtocolJson.Options);

		Assert.That(arguments!.Data, Is.Null);
	}

	[Test]
	public void The_host_writes_null_data_as_json_null_and_absent_data_not_at_all()
	{
		using var nullDocument = JsonDocument.Parse("null");
		var withNull = new UiSessionEventArguments
		{
			SessionId = "s",
			NodeId = "n",
			Name = "change",
			Data = nullDocument.RootElement
		};
		var withoutData = withNull with { Data = null };

		Assert.Multiple(() =>
		{
			Assert.That(JsonSerializer.Serialize(withNull, PluginProtocolJson.Options),
				Is.EqualTo("""{"sessionId":"s","nodeId":"n","name":"change","data":null}"""));
			Assert.That(JsonSerializer.Serialize(withoutData, PluginProtocolJson.Options),
				Is.EqualTo("""{"sessionId":"s","nodeId":"n","name":"change"}"""));
		});
	}

	[Test]
	public void Event_data_still_crosses_the_protocol_verbatim()
	{
		const string wire = """{"sessionId":"s","nodeId":"n","name":"change","data":{"z":1.0,"a":"café a<b"}}""";

		var arguments = JsonSerializer.Deserialize<UiSessionEventArguments>(wire, PluginProtocolJson.Options);

		Assert.That(JsonSerializer.Serialize(arguments, PluginProtocolJson.Options), Is.EqualTo(wire));
	}
}
