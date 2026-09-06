using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Logging;

/// <summary>Round-trips LogPublishPayload through the wire serializer and guards the enum-as-integer
/// trap Level is especially exposed to - it looks like it wants to be an enum.</summary>
[TestFixture]
public class LogPublishDtoSerializationTests
{
	[Test]
	public void Payload_round_trips_with_every_field_intact_including_the_offset()
	{
		var payload = new LogPublishPayload
		{
			Events =
			[
				new LogEventDto
				{
					Timestamp = DateTimeOffset.Parse("2026-08-11T09:30:00+02:00", CultureInfo.InvariantCulture),
					Level = LogLevels.Warning,
					MessageTemplate = "Volume set to {Volume}",
					RenderedMessage = "Volume set to 42",
					SourceContext = "AudioPlugin.VolumeHandler",
					Properties = new Dictionary<string, string> { ["Volume"] = "42" },
				},
				new LogEventDto
				{
					Timestamp = DateTimeOffset.Parse("2026-08-11T09:31:00+02:00", CultureInfo.InvariantCulture),
					Level = LogLevels.Error,
					MessageTemplate = "Failed to reach device",
					RenderedMessage = "Failed to reach device",
					Exception = new LogExceptionDto
					{
						Type = "System.TimeoutException",
						Message = "The operation timed out",
						StackTrace = "   at AudioPlugin.Connect()",
						Inner = new LogExceptionDto
						{
							Type = "System.Net.Sockets.SocketException",
							Message = "Connection refused",
						},
					},
				},
			],
			Dropped = 3,
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<LogPublishPayload>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Events, Has.Count.EqualTo(2));
			Assert.That(actual.Dropped, Is.EqualTo(payload.Dropped));

			var first = actual.Events[0];
			var expectedFirst = payload.Events[0];
			Assert.That(first.Timestamp, Is.EqualTo(expectedFirst.Timestamp));
			Assert.That(first.Timestamp.Offset, Is.EqualTo(expectedFirst.Timestamp.Offset));
			Assert.That(first.Level, Is.EqualTo(expectedFirst.Level));
			Assert.That(first.MessageTemplate, Is.EqualTo(expectedFirst.MessageTemplate));
			Assert.That(first.RenderedMessage, Is.EqualTo(expectedFirst.RenderedMessage));
			Assert.That(first.SourceContext, Is.EqualTo(expectedFirst.SourceContext));
			Assert.That(first.Properties, Is.Not.Null);
			Assert.That(first.Properties!["Volume"], Is.EqualTo("42"));

			var second = actual.Events[1];
			Assert.That(second.Exception, Is.Not.Null);
			Assert.That(second.Exception!.Type, Is.EqualTo("System.TimeoutException"));
			Assert.That(second.Exception.Message, Is.EqualTo("The operation timed out"));
			Assert.That(second.Exception.StackTrace, Is.EqualTo("   at AudioPlugin.Connect()"));
			Assert.That(second.Exception.Inner, Is.Not.Null);
			Assert.That(second.Exception.Inner!.Type, Is.EqualTo("System.Net.Sockets.SocketException"));
			Assert.That(second.Exception.Inner.Message, Is.EqualTo("Connection refused"));
			Assert.That(second.Exception.Inner.Inner, Is.Null);
		});
	}

	[Test]
	public void Level_serializes_as_a_json_string_not_a_number()
	{
		var payload = new LogPublishPayload
		{
			Events =
			[
				new LogEventDto
				{
					Timestamp = DateTimeOffset.UtcNow,
					Level = LogLevels.Warning,
					MessageTemplate = "template",
					RenderedMessage = "rendered",
				},
			],
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);

		Assert.That(json, Does.Contain("\"level\":\"warning\""));
	}

	[Test]
	public void Property_names_are_camel_cased_on_the_wire()
	{
		var payload = new LogPublishPayload
		{
			Events =
			[
				new LogEventDto
				{
					Timestamp = DateTimeOffset.UtcNow,
					Level = LogLevels.Information,
					MessageTemplate = "template",
					RenderedMessage = "rendered",
					SourceContext = "Some.Context",
				},
			],
			Dropped = 1,
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"messageTemplate\""));
			Assert.That(json, Does.Contain("\"renderedMessage\""));
			Assert.That(json, Does.Contain("\"sourceContext\""));
			Assert.That(json, Does.Contain("\"dropped\""));
		});
	}
}
