using System.Text.Json;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class SocketIoFramesTests
{
	[Test]
	public void TryParse_reads_an_open_frame_with_no_namespace()
	{
		var ok = SocketIoFrames.TryParse("""0{"sid":"abc","pingInterval":25000,"pingTimeout":20000}""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Engine, Is.EqualTo(EngineIoType.Open));
			Assert.That(frame.Namespace, Is.Null);
			Assert.That(frame.Payload, Is.EqualTo("""{"sid":"abc","pingInterval":25000,"pingTimeout":20000}"""));
		});
	}

	[Test]
	public void TryReadOpen_reads_sid_and_ping_values()
	{
		var ok = SocketIoFrames.TryReadOpen(
			"""{"sid":"abc123","pingInterval":25000,"pingTimeout":20000,"upgrades":[]}""",
			out var sid,
			out var pingInterval,
			out var pingTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(sid, Is.EqualTo("abc123"));
			Assert.That(pingInterval, Is.EqualTo(TimeSpan.FromSeconds(25)));
			Assert.That(pingTimeout, Is.EqualTo(TimeSpan.FromSeconds(20)));
		});
	}

	[Test]
	public void TryReadOpen_falls_back_to_engine_io_defaults_when_ping_values_are_missing()
	{
		var ok = SocketIoFrames.TryReadOpen("""{"sid":"abc123"}""",
			out var sid,
			out var pingInterval,
			out var pingTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(sid, Is.EqualTo("abc123"));
			Assert.That(pingInterval, Is.EqualTo(TimeSpan.FromSeconds(25)));
			Assert.That(pingTimeout, Is.EqualTo(TimeSpan.FromSeconds(20)));
		});
	}

	[Test]
	public void TryReadOpen_returns_false_for_non_json()
	{
		Assert.That(SocketIoFrames.TryReadOpen("not json", out _, out _, out _), Is.False);
	}

	[Test]
	public void TryReadOpen_returns_false_for_an_empty_payload()
	{
		Assert.That(SocketIoFrames.TryReadOpen(string.Empty, out _, out _, out _), Is.False);
	}

	[Test]
	public void Connect_renders_the_namespace_and_token_exactly()
	{
		Assert.That(SocketIoFrames.Connect("t"), Is.EqualTo("""40/api/v1/realtime,{"token":"t"}"""));
	}

	[Test]
	public void Connect_escapes_a_token_that_needs_it()
	{
		const string token = "has\"quote";
		var rendered = SocketIoFrames.Connect(token);

		Assert.That(rendered, Does.StartWith("40/api/v1/realtime,"));

		using var document = JsonDocument.Parse(rendered["40/api/v1/realtime,".Length..]);
		Assert.That(document.RootElement.GetProperty("token").GetString(), Is.EqualTo(token));
	}

	[Test]
	public void Disconnect_keeps_its_trailing_comma()
	{
		Assert.That(SocketIoFrames.Disconnect(), Is.EqualTo("41/api/v1/realtime,"));
	}

	[Test]
	public void Pong_is_a_bare_engine_io_pong()
	{
		Assert.That(SocketIoFrames.Pong(), Is.EqualTo("3"));
	}

	[Test]
	public void TryParse_maps_a_bare_ping_to_type_ping()
	{
		var ok = SocketIoFrames.TryParse("2", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Engine, Is.EqualTo(EngineIoType.Ping));
		});
	}

	[Test]
	public void TryParse_reads_an_event_frame_with_name_and_argument()
	{
		var ok = SocketIoFrames.TryParse("""42/api/v1/realtime,["state-update",{"player":{}}]""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Engine, Is.EqualTo(EngineIoType.Message));
			Assert.That(frame.Socket, Is.EqualTo(SocketIoType.Event));
			Assert.That(frame.Namespace, Is.EqualTo("/api/v1/realtime"));
		});

		var eventOk = SocketIoFrames.TryReadEvent(frame.Payload, out var name, out var argument);

		Assert.Multiple(() =>
		{
			Assert.That(eventOk, Is.True);
			Assert.That(name, Is.EqualTo("state-update"));
			Assert.That(argument.GetProperty("player").ValueKind, Is.EqualTo(JsonValueKind.Object));
		});
	}

	[Test]
	public void TryReadEvent_reads_a_bare_string_argument_for_playlist_deleted()
	{
		var ok = SocketIoFrames.TryReadEvent("""["playlist-deleted","pl-1"]""", out var name, out var argument);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(name, Is.EqualTo("playlist-deleted"));
			Assert.That(argument.ValueKind, Is.EqualTo(JsonValueKind.String));
			Assert.That(argument.GetString(), Is.EqualTo("pl-1"));
		});
	}

	[Test]
	public void TryReadEvent_rejects_arity_one()
	{
		Assert.That(SocketIoFrames.TryReadEvent("""["only-a-name"]""", out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_arity_three()
	{
		Assert.That(SocketIoFrames.TryReadEvent("""["name",1,2]""", out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_a_non_array()
	{
		Assert.That(SocketIoFrames.TryReadEvent("""{"name":"x"}""", out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_a_non_string_name()
	{
		Assert.That(SocketIoFrames.TryReadEvent("[1,2]", out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_truncated_json()
	{
		Assert.That(SocketIoFrames.TryReadEvent("[\"state-update\"", out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_an_empty_payload()
	{
		Assert.That(SocketIoFrames.TryReadEvent(string.Empty, out _, out _), Is.False);
	}

	[Test]
	public void TryReadEvent_rejects_non_json()
	{
		Assert.That(SocketIoFrames.TryReadEvent("not json", out _, out _), Is.False);
	}

	[Test]
	public void TryParse_accepts_a_json_body_on_a_connect_ack()
	{
		var ok = SocketIoFrames.TryParse("""40/api/v1/realtime,{"sid":"xyz"}""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Socket, Is.EqualTo(SocketIoType.Connect));
			Assert.That(frame.Namespace, Is.EqualTo("/api/v1/realtime"));
			Assert.That(frame.Payload, Is.EqualTo("""{"sid":"xyz"}"""));
		});
	}

	[Test]
	public void TryParse_recognises_a_connect_error()
	{
		var ok = SocketIoFrames.TryParse("""44/api/v1/realtime,{"message":"UNAUTHENTICATED"}""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Socket, Is.EqualTo(SocketIoType.ConnectError));
		});
	}

	[Test]
	public void TryParse_reads_a_frame_on_another_namespace_but_it_is_not_the_realtime_namespace()
	{
		var ok = SocketIoFrames.TryParse("""42/other,["x"]""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Namespace, Is.EqualTo("/other"));
			Assert.That(SocketIoFrames.IsRealtimeNamespace(frame.Namespace), Is.False);
		});
	}

	[Test]
	public void TryParse_parses_and_discards_an_ack_id()
	{
		var ok = SocketIoFrames.TryParse("""43/api/v1/realtime,12["ok"]""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(frame.Socket, Is.EqualTo(SocketIoType.Ack));
			Assert.That(frame.Namespace, Is.EqualTo("/api/v1/realtime"));
			Assert.That(frame.Payload, Is.EqualTo("""["ok"]"""));
		});
	}

	[Test]
	public void TryParse_returns_false_for_an_empty_frame()
	{
		Assert.That(SocketIoFrames.TryParse(string.Empty, out _), Is.False);
	}

	[Test]
	public void TryParse_returns_false_for_an_unrecognised_engine_type()
	{
		Assert.That(SocketIoFrames.TryParse("9garbage", out _), Is.False);
	}

	[Test]
	public void TryParse_returns_false_for_a_message_with_no_socket_io_type()
	{
		Assert.That(SocketIoFrames.TryParse("4", out _), Is.False);
	}

	[Test]
	public void TryParse_returns_false_for_an_unrecognised_socket_io_type()
	{
		Assert.That(SocketIoFrames.TryParse("49garbage", out _), Is.False);
	}

	[Test]
	public void IsRealtimeNamespace_is_true_for_the_realtime_namespace()
	{
		Assert.That(SocketIoFrames.IsRealtimeNamespace("/api/v1/realtime"), Is.True);
	}

	[Test]
	public void IsRealtimeNamespace_is_false_for_null()
	{
		Assert.That(SocketIoFrames.IsRealtimeNamespace(null), Is.False);
	}
}
