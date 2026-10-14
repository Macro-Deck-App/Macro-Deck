using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class WidgetDataAndVariableHistoryWireTests
{
	[Test]
	public void A_widget_data_request_and_answer_keep_their_shape()
	{
		var request = JsonSerializer.Serialize(new WidgetsGetDataArguments { WidgetId = "w1" },
			PluginProtocolJson.Options);
		using var data = JsonDocument.Parse("""{"account":"111"}""");
		var answer = JsonSerializer.Serialize(new WidgetDataDto
				{ WidgetId = "w1", WidgetTypeId = "com.example::chat", Data = data.RootElement },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(request, Is.EqualTo("""{"widgetId":"w1"}"""));
			Assert.That(answer,
				Is.EqualTo("""{"widgetId":"w1","widgetTypeId":"com.example::chat","data":{"account":"111"}}"""));
		});
	}

	[Test]
	public void The_history_operations_keep_their_shape()
	{
		var open = JsonSerializer.Serialize(new VariablesHistoryOpenArguments { Name = "viewer_count", Capacity = 40 },
			PluginProtocolJson.Options);
		var opened = JsonSerializer.Serialize(new VariablesHistoryOpenResult { WindowId = "w" },
			PluginProtocolJson.Options);
		var window = JsonSerializer.Serialize(new VariablesHistoryWindowArguments { WindowId = "w" },
			PluginProtocolJson.Options);
		var read = JsonSerializer.Serialize(new VariablesHistoryReadResult { Values = [1.5, null, 3] },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(open, Is.EqualTo("""{"name":"viewer_count","capacity":40}"""));
			Assert.That(opened, Is.EqualTo("""{"windowId":"w"}"""));
			Assert.That(window, Is.EqualTo("""{"windowId":"w"}"""));
			Assert.That(read, Is.EqualTo("""{"values":[1.5,null,3]}"""));
		});
	}
}
