using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeAccountManagerTests
{
	private RecordingIntegrationConfig _config = null!;
	private YouTubeQuotaBudgets _budgets = null!;
	private YouTubeAccountManager _manager = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingIntegrationConfig();
		_budgets = new YouTubeQuotaBudgets(new YouTubeManualClock(YouTubeTestSupport.Now));
		_manager = YouTubeTestSupport.Manager(_ => new FakeYouTubeApiClient(), _budgets);
	}

	[TearDown]
	public void TearDown() => _manager.Dispose();

	[Test]
	public async Task Two_channels_are_kept_apart_and_identified_by_their_channel_id()
	{
		YouTubeTestSupport.AddChannel(_config, "UCfirst", "First Channel", "@firstchannel");
		YouTubeTestSupport.AddChannel(_config, "UCsecond", "Second Channel");

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections.Select(c => c.Account.ChannelId),
				Is.EqualTo(new[] { "UCfirst", "UCsecond" }));
			Assert.That(_manager.Resolve("UCsecond")!.Account.Title, Is.EqualTo("Second Channel"));
			Assert.That(_manager.AccountOptions().Select(option => option.Value),
				Is.EqualTo(new[] { "UCfirst", "UCsecond" }));
			Assert.That(_manager.Issues(), Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_channel_id_resolves_to_the_first_channel()
	{
		YouTubeTestSupport.AddChannel(_config, "UCfirst", "First");
		YouTubeTestSupport.AddChannel(_config, "UCsecond", "Second");

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Resolve(null)!.Account.ChannelId, Is.EqualTo("UCfirst"));
			Assert.That(_manager.Resolve(string.Empty)!.Account.ChannelId, Is.EqualTo("UCfirst"));
			Assert.That(_manager.Resolve("UCunknown"), Is.Null);
		});
	}

	[Test]
	public async Task Connecting_the_same_channel_again_keeps_the_newest_entry_and_reports_the_older_one()
	{
		YouTubeTestSupport.AddChannel(_config, "UCsame", "Channel",
			connectedAt: YouTubeTestSupport.Now.AddDays(-3), entryTitle: "YouTube (old)");
		YouTubeTestSupport.AddChannel(_config, "UCsame", "Channel",
			connectedAt: YouTubeTestSupport.Now, entryTitle: "YouTube (new)");

		await _manager.ReloadAsync(_config);

		var issue = _manager.Issues().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections, Has.Count.EqualTo(1));
			Assert.That(_manager.Connections[0].Account.ConnectedAt, Is.EqualTo(YouTubeTestSupport.Now));
			Assert.That(issue.Id, Is.EqualTo("duplicate-account:UCsame"));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("YouTube (old)"));
		});
	}

	[Test]
	public async Task An_incomplete_entry_is_skipped()
	{
		YouTubeTestSupport.AddChannel(_config, "UCgood", "Good");
		_config.AddEntry("broken",
			new Dictionary<string, string?>(StringComparer.Ordinal) { [YouTubeConfigKeys.ClientId] = "client" });

		await _manager.ReloadAsync(_config);

		Assert.That(_manager.Connections.Select(c => c.Account.ChannelId), Is.EqualTo(new[] { "UCgood" }));
	}

	[TestCase("@Cozy.Streams", "Whatever", "cozy_streams")]
	[TestCase(null, "Mein Kanal!", "mein_kanal")]
	[TestCase(null, "日本語", "chuc_ab_cd")]
	[TestCase("@", "", "chuc_ab_cd")]
	public async Task The_variable_key_comes_from_the_handle_then_the_title_then_the_channel_id(
		string? handle,
		string title,
		string expected)
	{
		YouTubeTestSupport.AddChannel(_config, "UC_Ab-Cd", title, handle);

		await _manager.ReloadAsync(_config);

		Assert.That(_manager.Connections[0].Account.VariableKey, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_key_another_channel_already_uses_falls_back_to_the_channel_id()
	{
		YouTubeTestSupport.AddChannel(_config, "UCone", "Gaming");
		YouTubeTestSupport.AddChannel(_config, "UCtwo", "Gaming!");

		await _manager.ReloadAsync(_config);

		Assert.That(_manager.Connections.Select(c => c.Account.VariableKey),
			Is.EqualTo(new[] { "gaming", "chuctwo" }));
	}

	[Test]
	public async Task Channels_of_one_google_project_share_one_quota_budget()
	{
		YouTubeTestSupport.AddChannel(_config, "UCone", "One", dailyQuota: "10000");
		YouTubeTestSupport.AddChannel(_config, "UCtwo", "Two", dailyQuota: "50000");
		YouTubeTestSupport.AddChannel(_config, "UCother", "Other", clientId: "other-project");

		await _manager.ReloadAsync(_config);

		var budgets = _manager.Connections.Select(c => c.Budget).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(budgets[0], Is.SameAs(budgets[1]));
			Assert.That(budgets[0].Limit, Is.EqualTo(50_000));
			Assert.That(budgets[2], Is.Not.SameAs(budgets[0]));
		});
	}

	[Test]
	public async Task Lowering_the_daily_quota_applies_on_the_next_reload()
	{
		var entry = YouTubeTestSupport.AddChannel(_config, "UCone", "One", dailyQuota: "50000");
		await _manager.ReloadAsync(_config);

		_config.Strings[(entry, YouTubeConfigKeys.DailyQuota)] = "8000";
		await _manager.ReloadAsync(_config);

		Assert.That(_manager.Connections.Single().Budget.Limit, Is.EqualTo(8_000));
	}

	[Test]
	public async Task A_used_up_quota_is_reported_once_per_google_project_without_a_fix_action()
	{
		YouTubeTestSupport.AddChannel(_config, "UCone", "One");
		YouTubeTestSupport.AddChannel(_config, "UCtwo", "Two");
		await _manager.ReloadAsync(_config);

		_manager.Connections[0].Budget.MarkExhausted();

		var issue = _manager.Issues().Single();
		Assert.Multiple(() =>
		{
			Assert.That(issue.Id, Does.StartWith("quota-exhausted:"));
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			Assert.That(TestLocalization.Resolve(issue.ActionLabel), Is.Null);
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("Pacific"));
		});
	}

	[Test]
	public async Task Chat_and_stats_accounts_use_the_channel_id_and_the_youtube_variable_prefix()
	{
		YouTubeTestSupport.AddChannel(_config, "UCfirst", "First", "@first");

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.ChatAccounts().Single().AccountId, Is.EqualTo("UCfirst"));
			Assert.That(_manager.StatsAccounts().Single().AccountId, Is.EqualTo("UCfirst"));
			Assert.That(_manager.StatsAccounts().Single().VariablePrefix, Is.EqualTo("youtube_first_"));
		});
	}
}
