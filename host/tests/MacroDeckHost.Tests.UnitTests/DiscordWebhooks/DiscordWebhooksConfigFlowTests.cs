using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.DiscordWebhooks.Configuration;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class DiscordWebhooksConfigFlowTests
{
	private static Task<ConfigFlowResult> Submit(string name, string url)
	{
		return new DiscordWebhooksConfigFlow().SubmitAsync("webhook",
			new Dictionary<string, object?>
			{
				[DiscordWebhooksConfigKeys.Name] = name, [DiscordWebhooksConfigKeys.Url] = url
			},
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);
	}

	[Test]
	public async Task The_url_field_is_a_secret()
	{
		var start = await new DiscordWebhooksConfigFlow().StartAsync(NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);

		var url = start.NextStep!.Fields.Single(field => field.Name == DiscordWebhooksConfigKeys.Url);
		Assert.That(url.Type, Is.EqualTo(ActionParameterType.Secret));
	}

	[TestCase("https://discord.com/api/webhooks/123/abc_DEF-1")]
	[TestCase("https://canary.discord.com/api/webhooks/123/abc")]
	[TestCase("https://ptb.discord.com/api/v10/webhooks/123/abc")]
	[TestCase("https://discordapp.com/api/webhooks/123/abc")]
	public async Task A_discord_webhook_url_is_saved_as_a_secret_under_the_given_name(string url)
	{
		var result = await Submit("  Announcements ", url);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Announcements"));
			Assert.That(result.Values![DiscordWebhooksConfigKeys.Url].IsSecret, Is.True);
			Assert.That(result.Values[DiscordWebhooksConfigKeys.Url].Value, Is.EqualTo(url));
			Assert.That(result.Values[DiscordWebhooksConfigKeys.Name].IsSecret, Is.False);
		});
	}

	[TestCase("http://discord.com/api/webhooks/123/abc")]
	[TestCase("https://evil.example/api/webhooks/123/abc")]
	[TestCase("https://discord.com.evil.example/api/webhooks/123/abc")]
	[TestCase("https://discord.com/api/webhooks/notanumber/abc")]
	[TestCase("https://discord.com/api/webhooks/123")]
	[TestCase("https://discord.com/channels/1/2")]
	[TestCase("https://user:pass@discord.com/api/webhooks/123/abc")]
	[TestCase("not a url")]
	public async Task Anything_but_a_discord_webhook_url_is_refused(string url)
	{
		var result = await Submit("Announcements", url);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.FieldErrors![DiscordWebhooksConfigKeys.Url]),
				Does.Contain("not a Discord webhook URL"));
		});
	}

	[Test]
	public async Task A_name_is_required()
	{
		var result = await Submit(" ", "https://discord.com/api/webhooks/123/abc");

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { DiscordWebhooksConfigKeys.Name }));
	}
}
