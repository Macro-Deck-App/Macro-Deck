using System.Text.Json;
using MacroDeckHost.Integrations.Streamerbot;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotEventPayloadTests
{
	[Test]
	public void Build_carries_the_envelope()
	{
		var payload = Build("Twitch", "Follow", """{ "user_name": "Ada" }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["source"], Is.EqualTo("Twitch"));
			Assert.That(payload["type"], Is.EqualTo("Follow"));
			Assert.That(payload["data"], Is.EqualTo("""{ "user_name": "Ada" }"""));
		});
	}

	[Test]
	public void Build_normalises_a_follow()
	{
		var payload = Build("Twitch",
			"Follow",
			"""{ "user_id": "1", "user_login": "ada", "user_name": "Ada", "followed_at": "2026-07-30T10:00:00Z" }""");

		Assert.That(payload["user"], Is.EqualTo("Ada"));
	}

	[Test]
	public void Build_normalises_a_sub()
	{
		var payload = Build("Twitch",
			"Sub",
			"""{ "userName": "ada", "displayName": "Ada", "message": "hi", "subTier": 1000 }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["user"], Is.EqualTo("Ada"));
			Assert.That(payload["message"], Is.EqualTo("hi"));
			Assert.That(payload["subTier"], Is.EqualTo(1000L));
		});
	}

	[Test]
	public void Build_normalises_a_chat_message_from_the_nested_object()
	{
		var payload = Build("Twitch",
			"ChatMessage",
			"""{ "message": { "username": "ada", "displayName": "Ada", "message": "hello", "bits": 0 } }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["user"], Is.EqualTo("Ada"));
			Assert.That(payload["message"], Is.EqualTo("hello"));
			Assert.That(payload["message.displayName"], Is.EqualTo("Ada"));
		});
	}

	[Test]
	public void Build_normalises_a_raid_from_the_raiding_broadcaster()
	{
		var payload = Build("Twitch",
			"Raid",
			"""{ "from_broadcaster_user_name": "Grace", "to_broadcaster_user_name": "Ada", "viewers": 42 }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["user"], Is.EqualTo("Grace"));
			Assert.That(payload["viewers"], Is.EqualTo(42L));
		});
	}

	[Test]
	public void Build_flattens_nested_objects_with_dotted_keys()
	{
		var payload = Build("Twitch",
			"RewardRedemption",
			"""{ "user_name": "Ada", "user_input": "go", "reward": { "title": "Hydrate", "cost": 500 } }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["reward.title"], Is.EqualTo("Hydrate"));
			Assert.That(payload["reward.cost"], Is.EqualTo(500L));
			Assert.That(payload["message"], Is.EqualTo("go"));
		});
	}

	[Test]
	public void Build_keeps_primitive_types()
	{
		var payload = Build("Misc", "Test", """{ "flag": true, "count": 3, "ratio": 1.5, "nothing": null }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["flag"], Is.EqualTo(true));
			Assert.That(payload["count"], Is.EqualTo(3L));
			Assert.That(payload["ratio"], Is.EqualTo(1.5d));
			Assert.That(payload["nothing"], Is.Null);
		});
	}

	[Test]
	public void Build_carries_arrays_as_json_text()
	{
		var payload = Build("Twitch", "Sub", """{ "userName": "Ada", "emotes": [ { "name": "Kappa" } ] }""");

		Assert.That(payload["emotes"], Is.EqualTo("""[ { "name": "Kappa" } ]"""));
	}

	[Test]
	public void Build_never_lets_the_payload_overwrite_the_envelope()
	{
		var payload = Build("Twitch", "Custom", """{ "source": "spoofed", "type": "spoofed", "data": "spoofed" }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload["source"], Is.EqualTo("Twitch"));
			Assert.That(payload["type"], Is.EqualTo("Custom"));
			Assert.That(payload["data"] as string, Does.Contain("spoofed"));
		});
	}

	[Test]
	public void Build_prefers_the_payloads_own_user_field()
	{
		var payload = Build("Custom", "Event", """{ "user": "Ada", "displayName": "somebody else" }""");

		Assert.That(payload["user"], Is.EqualTo("Ada"));
	}

	[Test]
	public void Build_tolerates_an_event_without_data()
	{
		var payload = StreamerbotEventPayload.Build("Misc", "StreamerbotStarted", default);

		Assert.Multiple(() =>
		{
			Assert.That(payload["type"], Is.EqualTo("StreamerbotStarted"));
			Assert.That(payload["data"], Is.EqualTo(string.Empty));
			Assert.That(payload.ContainsKey("user"), Is.False);
		});
	}

	[Test]
	public void Build_stops_at_the_parameter_limit()
	{
		var fields = string.Join(",", Enumerable.Range(0, 200).Select(i => $"\"f{i}\": {i}"));
		var payload = Build("Misc", "Test", $"{{ {fields} }}");

		Assert.That(payload, Has.Count.LessThanOrEqualTo(StreamerbotEventPayload.MaxParameters + 3));
	}

	[Test]
	public void Build_stops_at_the_depth_limit()
	{
		var payload = Build("Misc", "Test", """{ "a": { "b": { "c": { "d": "too deep" } } } }""");

		Assert.Multiple(() =>
		{
			Assert.That(payload.Keys, Has.No.Member("a.b.c.d"));
			Assert.That(payload["a.b.c"], Is.EqualTo("""{ "d": "too deep" }"""));
		});
	}

	private static IReadOnlyDictionary<string, object?> Build(string source, string type, string data)
	{
		using var document = JsonDocument.Parse(data);
		return StreamerbotEventPayload.Build(source, type, document.RootElement);
	}
}
