using System.Globalization;
using System.Text;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchIntegrationTests
{
	private TwitchIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_integration = new TwitchIntegration();
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	[Test]
	public void The_integration_is_identified()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.twitch"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Twitch"));
			Assert.That(typeof(TwitchIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Log.Logger);

		var twitch = discovered.OfType<TwitchIntegration>().SingleOrDefault();

		Assert.That(twitch, Is.Not.Null, "the Twitch integration was not discovered");
	}

	[Test]
	public void The_brand_icon_is_an_svg()
	{
		var icon = Encoding.UTF8.GetString(_integration.GetIcon());

		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(icon, Does.Contain("<svg"));
		});
	}

	[Test]
	public void Shutdown_is_safe_without_a_connection()
	{
		Assert.DoesNotThrowAsync(() => _integration.ShutdownAsync());
	}

	[Test]
	public void DeclaredVariables_is_a_template_when_nothing_is_configured()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.VariablesDependOnConfiguration, Is.True);
			Assert.That(_integration.Variables, Is.Empty);
			Assert.That(_integration.DeclaredVariables, Is.Not.Empty);
			Assert.That(_integration.DeclaredVariables.Select(v => v.Name),
				Is.All.Contains(VariableNameTemplate.Placeholder("account")));
		});
	}

	[Test]
	public async Task DeclaredVariables_is_the_real_per_account_set_once_connected()
	{
		var manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			new LoggerConfiguration().CreateLogger(),
			(_, _) => new FakeTwitchHelixClient());
		var config = new RecordingIntegrationConfig();
		config.AddEntry("Twitch (streamer)",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = "111",
				[TwitchConfigKeys.Login] = "streamer",
				[TwitchConfigKeys.DisplayName] = "Streamer",
				[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
				[TwitchConfigKeys.ExpiresAt] =
					DateTimeOffset.UtcNow.AddHours(4).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});
		await manager.ReloadAsync(config);

		using var integration = new TwitchIntegration(manager);

		var configuration = integration.DeclaredVariables[0].Configuration;
		Assert.Multiple(() =>
		{
			Assert.That(integration.VariablesDependOnConfiguration, Is.True);
			Assert.That(configuration?.Key, Is.EqualTo("streamer"));
			Assert.That(configuration?.Name.Literal, Is.EqualTo("Streamer"));
			Assert.That(integration.DeclaredVariables,
				Is.EqualTo(TwitchVariables.Declare("streamer", configuration)));
			Assert.That(integration.DeclaredVariables.Select(v => v.Name),
				Has.None.Contains(VariableNameTemplate.PlaceholderStart));
		});
	}
}
