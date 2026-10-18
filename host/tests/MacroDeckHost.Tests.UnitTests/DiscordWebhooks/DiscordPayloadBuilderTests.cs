using System.Text.Json;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class DiscordPayloadBuilderTests
{
	private static readonly DateTimeOffset _now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

	private static JsonElement Payload(Dictionary<string, string> map)
	{
		var payload = DiscordPayloadBuilder.Build(DiscordMessageMap.Parse(map), _now);
		return JsonSerializer.SerializeToElement(payload, DiscordWebhookPayload.JsonOptions);
	}

	[Test]
	public void A_message_pings_nobody_unless_mentions_are_allowed()
	{
		var payload = Payload(new Dictionary<string, string> { ["content"] = "Hi <@123> @everyone" });

		var mentions = payload.GetProperty("allowed_mentions");
		Assert.Multiple(() =>
		{
			Assert.That(payload.GetProperty("content").GetString(), Is.EqualTo("Hi <@123> @everyone"));
			Assert.That(mentions.GetProperty("parse").GetArrayLength(), Is.Zero);
			Assert.That(mentions.TryGetProperty("users", out _), Is.False);
			Assert.That(mentions.TryGetProperty("roles", out _), Is.False);
		});
	}

	[Test]
	public void Allowed_mentions_carry_the_chosen_types_and_id_lists()
	{
		var payload = Payload(new Dictionary<string, string>
		{
			["content"] = "x",
			["allowed.parse"] = "roles,everyone",
			["allowed.users"] = "111, 222"
		});

		var mentions = payload.GetProperty("allowed_mentions");
		Assert.Multiple(() =>
		{
			Assert.That(mentions.GetProperty("parse").EnumerateArray().Select(e => e.GetString()),
				Is.EqualTo(new[] { "roles", "everyone" }));
			Assert.That(mentions.GetProperty("users").EnumerateArray().Select(e => e.GetString()),
				Is.EqualTo(new[] { "111", "222" }));
		});
	}

	[Test]
	public void Embeds_and_their_fields_become_discord_embed_objects()
	{
		var payload = Payload(new Dictionary<string, string>
		{
			["username"] = "Deck",
			["avatar_url"] = "https://example.invalid/a.png",
			["embeds.0.title"] = "Live",
			["embeds.0.description"] = "Come **watch**",
			["embeds.0.url"] = "https://example.invalid/live",
			["embeds.0.color"] = "#5865f2",
			["embeds.0.timestamp"] = "2026-01-02T03:04:05Z",
			["embeds.0.author.name"] = "Streamer",
			["embeds.0.author.url"] = "https://example.invalid/me",
			["embeds.0.author.icon_url"] = "https://example.invalid/me.png",
			["embeds.0.footer.text"] = "via Macro Deck",
			["embeds.0.footer.icon_url"] = "https://example.invalid/f.png",
			["embeds.0.image"] = "https://example.invalid/big.png",
			["embeds.0.thumbnail"] = "attachment://small.png",
			["embeds.0.fields.0.name"] = "Game",
			["embeds.0.fields.0.value"] = "Chess",
			["embeds.0.fields.0.inline"] = "true",
			["embeds.0.fields.1.name"] = "Viewers",
			["embeds.0.fields.1.value"] = "42",
			["embeds.1.timestamp"] = "now",
			["embeds.1.title"] = "Second"
		});

		var embeds = payload.GetProperty("embeds");
		var first = embeds[0];
		var fields = first.GetProperty("fields");
		Assert.Multiple(() =>
		{
			Assert.That(payload.GetProperty("username").GetString(), Is.EqualTo("Deck"));
			Assert.That(payload.GetProperty("avatar_url").GetString(), Is.EqualTo("https://example.invalid/a.png"));
			Assert.That(embeds.GetArrayLength(), Is.EqualTo(2));
			Assert.That(first.GetProperty("title").GetString(), Is.EqualTo("Live"));
			Assert.That(first.GetProperty("description").GetString(), Is.EqualTo("Come **watch**"));
			Assert.That(first.GetProperty("url").GetString(), Is.EqualTo("https://example.invalid/live"));
			Assert.That(first.GetProperty("color").GetInt32(), Is.EqualTo(0x5865F2));
			Assert.That(first.GetProperty("timestamp").GetString(), Is.EqualTo("2026-01-02T03:04:05.000Z"));
			Assert.That(first.GetProperty("author").GetProperty("name").GetString(), Is.EqualTo("Streamer"));
			Assert.That(first.GetProperty("author").GetProperty("icon_url").GetString(),
				Is.EqualTo("https://example.invalid/me.png"));
			Assert.That(first.GetProperty("footer").GetProperty("text").GetString(), Is.EqualTo("via Macro Deck"));
			Assert.That(first.GetProperty("image").GetProperty("url").GetString(),
				Is.EqualTo("https://example.invalid/big.png"));
			Assert.That(first.GetProperty("thumbnail").GetProperty("url").GetString(),
				Is.EqualTo("attachment://small.png"));
			Assert.That(fields[0].GetProperty("inline").GetBoolean(), Is.True);
			Assert.That(fields[1].GetProperty("name").GetString(), Is.EqualTo("Viewers"));
			Assert.That(fields[1].GetProperty("inline").GetBoolean(), Is.False);
			Assert.That(embeds[1].GetProperty("timestamp").GetString(),
				Is.EqualTo("2026-10-18T09:30:00.000Z"),
				"time of sending is the moment the payload is built");
		});
	}

	[Test]
	public void Blank_values_are_left_out_of_the_payload()
	{
		var payload = Payload(new Dictionary<string, string> { ["content"] = "hi", ["username"] = "  " });

		Assert.Multiple(() =>
		{
			Assert.That(payload.TryGetProperty("username", out _), Is.False);
			Assert.That(payload.TryGetProperty("embeds", out _), Is.False);
		});
	}
}
