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
	public void An_open_travels_with_camel_case_keys()
	{
		var arguments = new VideoStreamSessionOpenArguments
		{
			SessionId = "session",
			ProviderId = "obs",
			StreamId = "Scene 1",
			AcceptedTransports = ["hls", "mjpeg"]
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VideoStreamSessionOpenArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"acceptedTransports\":[\"hls\",\"mjpeg\"]"));
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.StreamId, Is.EqualTo("Scene 1"));
			Assert.That(actual.AcceptedTransports, Is.EqualTo(arguments.AcceptedTransports));
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
		var resume = JsonSerializer.Deserialize<VideoStreamSessionResumeResult>("{}", PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(stream!.State, Is.EqualTo("Connected"));
			Assert.That(stream.HasAudio, Is.False);
			Assert.That(stream.Width, Is.Null);
			Assert.That(resume!.Description, Is.Null);
		});
	}

	[Test]
	public void A_session_description_carries_only_the_transport_and_the_url()
	{
		var json = JsonSerializer.Serialize(new VideoStreamSessionOpenResult
		{
			Description = new VideoStreamSessionDescriptionDto { Transport = "hls", Url = "http://127.0.0.1:9/a.m3u8" },
			RegistrationId = "registration"
		}, PluginProtocolJson.Options);

		Assert.That(json,
			Does.Contain("""{"transport":"hls","url":"http://127.0.0.1:9/a.m3u8"}"""));
	}

	[Test]
	public void An_older_peers_description_and_open_with_members_that_no_longer_exist_still_deserialize()
	{
		var result = JsonSerializer.Deserialize<VideoStreamSessionOpenResult>(
			"""
			{"registrationId":"r","description":{"transport":"mjpeg","url":"http://127.0.0.1:9/cam.mjpg",
			"parameters":{"room":"kitchen"},"payload":"v=0","expiresAt":"2026-01-01T00:00:00+00:00"}}
			""",
			PluginProtocolJson.Options);
		var open = JsonSerializer.Deserialize<VideoStreamSessionOpenArguments>(
			"""
			{"sessionId":"s","providerId":"cam","streamId":"main","acceptedTransports":["hls"],
			"consumer":{"deviceId":"phone","hostAddress":"http://10.0.0.2:8191/","connectionKind":"UsbTunnel"}}
			""",
			PluginProtocolJson.Options);
		var update = JsonSerializer.Deserialize<VideoStreamsSessionUpdateArguments>(
			"""{"sessionId":"s","state":"Active","description":{"transport":"hls","url":"http://h/a.m3u8","payload":"x"}}""",
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(result!.Description.Transport, Is.EqualTo("mjpeg"));
			Assert.That(result.Description.Url, Is.EqualTo("http://127.0.0.1:9/cam.mjpg"));
			Assert.That(open!.SessionId, Is.EqualTo("s"));
			Assert.That(open.AcceptedTransports.Single(), Is.EqualTo("hls"));
			Assert.That(update!.Description!.Url, Is.EqualTo("http://h/a.m3u8"));
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
