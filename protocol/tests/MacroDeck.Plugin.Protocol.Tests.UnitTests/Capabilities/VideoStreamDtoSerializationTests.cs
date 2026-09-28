using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class VideoStreamDtoSerializationTests
{
	[Test]
	public void An_open_travels_with_camel_case_keys_and_its_enums_as_member_names()
	{
		var arguments = new VideoStreamSessionOpenArguments
		{
			SessionId = "session",
			ProviderId = "obs",
			StreamId = "Scene 1",
			AcceptedTransports = ["whep", "mjpeg"],
			Consumer = new VideoStreamConsumerDto
			{
				DeviceId = "device", HostAddress = "http://192.168.1.2:8191/", ConnectionKind = "UsbTunnel"
			}
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VideoStreamSessionOpenArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"acceptedTransports\":[\"whep\",\"mjpeg\"]"));
			Assert.That(json, Does.Contain("\"connectionKind\":\"UsbTunnel\""));
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.StreamId, Is.EqualTo("Scene 1"));
			Assert.That(actual.AcceptedTransports, Is.EqualTo(arguments.AcceptedTransports));
			Assert.That(actual.Consumer, Is.EqualTo(arguments.Consumer));
		});
	}

	[Test]
	public void A_describe_result_round_trips_and_omits_what_is_absent()
	{
		var payload = new VideoStreamProviderDescribePayload
		{
			Providers =
			[
				new VideoStreamProviderDto
				{
					Id = "obs", Name = LocalizedText.FromLiteral("OBS"), RegistrationId = "registration"
				}
			]
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VideoStreamProviderDescribePayload>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("description"));
			Assert.That(actual!.Providers, Has.Count.EqualTo(1));
			Assert.That(actual.Providers[0].RegistrationId, Is.EqualTo("registration"));
		});
	}

	[Test]
	public void Enum_values_a_reader_does_not_know_still_deserialize()
	{
		var stream = JsonSerializer.Deserialize<VideoStreamDescriptorDto>(
			"""{"id":"cam","name":"Camera","state":"Degraded","futureField":1}""", PluginProtocolJson.Options);
		var update = JsonSerializer.Deserialize<VideoStreamsSessionUpdateArguments>(
			"""{"sessionId":"s","state":"Buffering","reason":"Overheated"}""", PluginProtocolJson.Options);
		var close = JsonSerializer.Deserialize<VideoStreamSessionCloseArguments>(
			"""{"sessionId":"s","providerId":"cam","reason":"Unplugged"}""", PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(stream!.State, Is.EqualTo("Degraded"));
			Assert.That(update!.State, Is.EqualTo("Buffering"));
			Assert.That(update.Reason, Is.EqualTo("Overheated"));
			Assert.That(close!.Reason, Is.EqualTo("Unplugged"));
		});
	}

	[Test]
	public void Omitted_optional_values_read_as_their_documented_defaults()
	{
		var stream = JsonSerializer.Deserialize<VideoStreamDescriptorDto>(
			"""{"id":"cam","name":"Camera"}""", PluginProtocolJson.Options);
		var consumer = JsonSerializer.Deserialize<VideoStreamConsumerDto>("{}", PluginProtocolJson.Options);
		var resume = JsonSerializer.Deserialize<VideoStreamSessionResumeResult>("{}", PluginProtocolJson.Options);
		var signal = JsonSerializer.Deserialize<VideoStreamSessionSignalResult>("{}", PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(stream!.State, Is.EqualTo("Connected"));
			Assert.That(stream.HasAudio, Is.False);
			Assert.That(stream.Width, Is.Null);
			Assert.That(consumer!.ConnectionKind, Is.EqualTo("Network"));
			Assert.That(resume!.Description, Is.Null);
			Assert.That(signal!.Signal, Is.Null);
		});
	}

	[Test]
	public void A_session_description_keeps_its_opaque_values_unchanged()
	{
		var description = new VideoStreamSessionDescriptionDto
		{
			Transport = "webrtc",
			Payload = "v=0\r\no=- 1 1 IN IP4 127.0.0.1",
			Parameters = new Dictionary<string, string> { ["iceServers"] = "[]" },
			ExpiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
		};

		var json = JsonSerializer.Serialize(new VideoStreamSessionOpenResult
		{
			Description = description, RegistrationId = "registration"
		}, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VideoStreamSessionOpenResult>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actual!.Description.Payload, Is.EqualTo(description.Payload));
			Assert.That(actual.Description.Parameters, Is.EqualTo(description.Parameters));
			Assert.That(actual.Description.ExpiresAt, Is.EqualTo(description.ExpiresAt));
		});
	}

	[TestCase("webrtc", true)]
	[TestCase("x-mjpeg+http.v2", true)]
	[TestCase("WebRTC", false)]
	[TestCase("", false)]
	[TestCase("web rtc", false)]
	[TestCase("abcdefghijklmnopqrstuvwxyz0123456", false)]
	public void Transport_tokens_are_short_lowercase_words(string transport, bool valid)
		=> Assert.That(VideoStreamLimits.IsValidTransport(transport), Is.EqualTo(valid));

	[TestCase("Scene 1", true)]
	[TestCase("", false)]
	[TestCase("line\nbreak", false)]
	public void Stream_ids_allow_spaces_but_no_control_characters(string streamId, bool valid)
		=> Assert.That(VideoStreamLimits.IsValidStreamId(streamId), Is.EqualTo(valid));

	[Test]
	public void A_stream_id_longer_than_the_limit_is_invalid()
		=> Assert.That(VideoStreamLimits.IsValidStreamId(new string('a', VideoStreamLimits.MaxStreamIdLength + 1)),
			Is.False);
}
