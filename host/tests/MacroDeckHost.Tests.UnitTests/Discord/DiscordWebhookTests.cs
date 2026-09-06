using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Integrations.Discord.Actions;
using MacroDeckHost.Integrations.Discord.Webhooks;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordWebhookTests
{
	private const string ValidUrl = "https://discord.com/api/webhooks/123/token";

	private static readonly DiscordWebhookRequest _hello = new() { Content = "hello" };

	private static readonly JsonSerializerOptions _json = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	[Test]
	public void A_discord_webhook_url_is_accepted()
	{
		Assert.That(DiscordWebhookClient.Validate(ValidUrl, _hello), Is.Null);
	}

	[TestCase("https://canary.discord.com/api/webhooks/1/t")]
	[TestCase("https://ptb.discord.com/api/webhooks/1/t")]
	[TestCase("https://discordapp.com/api/webhooks/1/t")]
	public void The_other_discord_hosts_are_accepted(string url)
	{
		Assert.That(DiscordWebhookClient.Validate(url, _hello), Is.Null);
	}

	[TestCase("https://example.invalid/api/webhooks/1/t")]
	[TestCase("https://discord.com.example.invalid/api/webhooks/1/t")]
	[TestCase("https://evil-discord.com/api/webhooks/1/t")]
	public void A_non_discord_host_is_refused(string url)
	{
		Assert.That(DiscordWebhookClient.Validate(url, _hello), Does.Contain("not a Discord webhook URL"));
	}

	[TestCase("http://discord.com/api/webhooks/1/t")]
	[TestCase("file:///etc/passwd")]
	[TestCase("not a url")]
	public void A_non_https_url_is_refused(string url)
	{
		Assert.That(DiscordWebhookClient.Validate(url, _hello), Does.Contain("https://"));
	}

	[Test]
	public void A_missing_url_is_refused()
	{
		Assert.That(DiscordWebhookClient.Validate("   ", _hello), Does.Contain("No webhook URL"));
	}

	[Test]
	public void An_empty_message_is_refused_before_it_is_sent()
	{
		Assert.That(DiscordWebhookClient.Validate(ValidUrl, new DiscordWebhookRequest()),
			Does.Contain("message is empty"));
	}

	[Test]
	public void An_embed_alone_is_a_valid_message()
	{
		var request = new DiscordWebhookRequest
		{
			Embeds = [new DiscordWebhookEmbed { Title = "Live" }]
		};

		Assert.That(DiscordWebhookClient.Validate(ValidUrl, request), Is.Null);
	}

	[Test]
	public void The_request_body_carries_the_configured_message()
	{
		var request = ExecuteWebhookActionDefinition.BuildRequest(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["content"] = "going live",
			["username"] = "Macro Deck",
			["avatarUrl"] = "https://example.invalid/a.png",
			["tts"] = true
		});

		Assert.Multiple(() =>
		{
			Assert.That(request.Content, Is.EqualTo("going live"));
			Assert.That(request.Username, Is.EqualTo("Macro Deck"));
			Assert.That(request.AvatarUrl, Is.EqualTo("https://example.invalid/a.png"));
			Assert.That(request.Tts, Is.True);
			Assert.That(request.Embeds, Is.Null);
		});
	}

	[Test]
	public void Blank_fields_are_omitted_rather_than_sent_as_empty_strings()
	{
		var request = ExecuteWebhookActionDefinition.BuildRequest(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["content"] = "hi",
			["username"] = "   ",
			["tts"] = false
		});

		Assert.Multiple(() =>
		{
			Assert.That(request.Username, Is.Null);
			Assert.That(request.Tts, Is.Null, "an unset toggle should not appear in the payload");
		});
	}

	[Test]
	public void The_embed_fields_are_collected_into_one_embed()
	{
		var request = ExecuteWebhookActionDefinition.BuildRequest(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["embedTitle"] = "Stream started",
			["embedDescription"] = "Come watch",
			["embedUrl"] = "https://example.invalid/live",
			["embedColor"] = "#5865f2",
			["embedImageUrl"] = "https://example.invalid/big.png",
			["embedThumbnailUrl"] = "https://example.invalid/small.png",
			["embedFooter"] = "via Macro Deck"
		});

		var embed = request.Embeds!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(embed.Title, Is.EqualTo("Stream started"));
			Assert.That(embed.Description, Is.EqualTo("Come watch"));
			Assert.That(embed.Url, Is.EqualTo("https://example.invalid/live"));
			Assert.That(embed.Color, Is.EqualTo(0x5865F2));
			Assert.That(embed.Image!.Url, Is.EqualTo("https://example.invalid/big.png"));
			Assert.That(embed.Thumbnail!.Url, Is.EqualTo("https://example.invalid/small.png"));
			Assert.That(embed.Footer!.Text, Is.EqualTo("via Macro Deck"));
		});
	}

	[Test]
	public void An_embed_with_only_a_colour_is_dropped()
	{
		var request = ExecuteWebhookActionDefinition.BuildRequest(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["content"] = "hi",
			["embedColor"] = "#ff0000"
		});

		Assert.That(request.Embeds, Is.Null);
	}

	[TestCase("#5865f2", 0x5865F2)]
	[TestCase("5865F2", 0x5865F2)]
	[TestCase("#000000", 0)]
	[TestCase("#ffffffff", 0xFFFFFF)]
	public void A_colour_is_converted_to_the_integer_discord_expects(string value, int expected)
	{
		Assert.That(ExecuteWebhookActionDefinition.ParseColor(value), Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("#12")]
	[TestCase("rebeccapurple")]
	[TestCase("#gggggg")]
	public void An_unparseable_colour_is_dropped_rather_than_guessed(string value)
	{
		Assert.That(ExecuteWebhookActionDefinition.ParseColor(value), Is.Null);
	}

	[Test]
	public void The_serialized_body_omits_everything_that_was_not_configured()
	{
		var request = ExecuteWebhookActionDefinition.BuildRequest(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["content"] = "hi"
		});

		var json = JsonSerializer.Serialize(request, _json);

		Assert.That(json, Is.EqualTo("""{"content":"hi"}"""));
	}
}
