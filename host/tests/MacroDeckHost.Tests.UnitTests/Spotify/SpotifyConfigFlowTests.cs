using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyConfigFlowTests
{
	[Test]
	public async Task The_credentials_step_shows_the_redirect_uri_as_a_copy_value_not_in_prose()
	{
		var flow = new SpotifyConfigFlow();
		var context = new StubContext(new StubOAuthSession("http://127.0.0.1:44556/api/integrations/oauth/callback"));

		var result = await flow.StartAsync(context, CancellationToken.None);

		var step = result.NextStep!;
		Assert.Multiple(() =>
		{
			Assert.That(step.Instructions, Has.Count.EqualTo(3));
			Assert.That(step.Instructions[1].Values, Has.Count.EqualTo(1));
			Assert.That(TestLocalization.Resolve(step.Instructions[1].Values[0].Label), Is.EqualTo("Redirect URI"));
			Assert.That(step.Instructions[1].Values[0].Value, Is.EqualTo(context.OAuth.RedirectUri));
			Assert.That(TestLocalization.Resolve(step.Description), Does.Not.Contain(context.OAuth.RedirectUri));
		});
	}

	[Test]
	public async Task The_credentials_step_keeps_the_developer_dashboard_link()
	{
		var flow = new SpotifyConfigFlow();
		var context = new StubContext(new StubOAuthSession("http://127.0.0.1:44556/api/integrations/oauth/callback"));

		var result = await flow.StartAsync(context, CancellationToken.None);

		var link = result.NextStep!.Links.Single();
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(link.Label), Is.EqualTo("Open Spotify Developer Dashboard"));
			Assert.That(link.Url, Is.EqualTo("https://developer.spotify.com/dashboard"));
		});
	}

	private sealed class StubContext(IOAuthSession oauth) : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = oauth;
	}

	private sealed class StubOAuthSession(string redirectUri) : IOAuthSession
	{
		public string RedirectUri => redirectUri;

		public string State => "unused";

		public string? AuthorizationCode => null;
	}
}
