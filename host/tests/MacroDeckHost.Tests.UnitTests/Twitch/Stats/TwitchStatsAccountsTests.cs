using MacroDeck.Sdk.Issues;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Tests.UnitTests.Twitch.Chat;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Variables;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Stats;

[TestFixture]
internal sealed class TwitchStatsAccountsTests
{
	[Test]
	public void The_hub_announces_a_change_only_when_the_account_list_differs()
	{
		var hub = new TwitchStatsAccountsHub();
		var changes = 0;
		hub.Changed += (_, _) => changes++;
		TwitchStatsAccount[] accounts = [new("111", "Streamer", "streamer")];

		hub.SetAccounts(accounts);
		hub.SetAccounts([new TwitchStatsAccount("111", "Streamer", "streamer")]);
		hub.SetAccounts([new TwitchStatsAccount("111", "Streamer", "renamed")]);
		hub.SetAccounts([]);
		hub.SetAccounts([]);

		Assert.Multiple(() =>
		{
			Assert.That(changes, Is.EqualTo(3));
			Assert.That(hub.Accounts, Is.Empty);
		});
	}

	[Test]
	public async Task The_integration_reports_its_accounts_with_their_variable_keys_and_clears_them_on_shutdown()
	{
		var hub = new TwitchStatsAccountsHub();
		var config = new RecordingIntegrationConfig();
		TwitchChatTestSupport.AddAccount(config, "111", "streamer");
		var manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			Logger.None,
			(_, _) => new FakeTwitchHelixClient());
		using var integration = new TwitchIntegration(manager);
		IntegrationGatewayBinder.Bind(integration,
			null!,
			null!,
			new InMemoryVariableBindingStore(),
			new VariableRefreshSignal(),
			twitchStatsSink: hub);
		await manager.ReloadAsync(config);
		var reported = manager.StatsAccounts();
		hub.SetAccounts(reported);

		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(reported.Select(account => (account.UserId, account.VariableKey)),
				Is.EqualTo(new[] { ("111", "streamer") }));
			Assert.That(hub.Accounts, Is.Empty, "the widget must see no account after shutdown");
		});
	}

	[Test]
	public async Task The_stats_widget_is_offered_with_an_account_next_to_the_chat_widget()
	{
		var config = new RecordingIntegrationConfig();
		var manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			Logger.None,
			(_, _) => new FakeTwitchHelixClient());
		using var integration = new TwitchIntegration(manager);
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		var host = new WidgetTypeProviderHost(registry, TimeProvider.System, Logger.None);

		await manager.ReloadAsync(config);
		Assert.That(integration.GetWidgetTypes(), Is.Empty);

		TwitchChatTestSupport.AddAccount(config, "111", "streamer");
		await manager.ReloadAsync(config);
		await host.StartAsync(integration);

		Assert.Multiple(() =>
		{
			Assert.That(registry.TryResolve(TwitchStatsWidgetType.QualifiedId, out var entry), Is.True);
			Assert.That(entry.Descriptor.HasConfiguration, Is.True);
			Assert.That(TestLocalization.Resolve(entry.Descriptor.Name), Is.EqualTo("Twitch Stream Stats"));
			Assert.That(registry.IsRegistered(TwitchChatWidgetType.QualifiedId), Is.True);
		});

		manager.Dispose();
	}

	[Test]
	public async Task Resolving_the_chatters_issue_starts_the_reconnect_flow()
	{
		using var integration = new TwitchIntegration();

		var resolution = await integration.ResolveIssueAsync("missing-chatters-scope:111");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}
}
