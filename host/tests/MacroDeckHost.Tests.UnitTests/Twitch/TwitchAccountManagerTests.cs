using System.Globalization;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchAccountManagerTests
{
	private static readonly string[] _bothLogins = ["streamer", "botaccount"];
	private static readonly string[] _bothUserIds = ["111", "222"];
	private static readonly string[] _onlyFirstUserId = ["111"];

	private RecordingIntegrationConfig _config = null!;
	private TwitchAccountManager _manager = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingIntegrationConfig();
		_manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			SilentLogger(),
			(_, _) => new FakeTwitchHelixClient());
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
	}

	[Test]
	public async Task Two_accounts_are_kept_apart()
	{
		AddAccount("111", "streamer");
		AddAccount("222", "botaccount");

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections.Select(c => c.Account.Login), Is.EquivalentTo(_bothLogins));
			Assert.That(_manager.Connections.Select(c => c.Account.VariableKey), Is.EquivalentTo(_bothLogins));
			Assert.That(_manager.Issues(), Is.Empty);
		});
	}

	[Test]
	public async Task The_instance_id_is_the_twitch_user_id_not_the_entry_id()
	{
		var entryId = AddAccount("111", "streamer");

		await _manager.ReloadAsync(_config);

		var account = _manager.Connections[0].Account;
		Assert.Multiple(() =>
		{
			Assert.That(account.UserId, Is.EqualTo("111"));
			Assert.That(account.EntryId, Is.EqualTo(entryId));
			Assert.That(_manager.Resolve("111"), Is.Not.Null);
			Assert.That(_manager.Resolve(entryId.ToString()), Is.Null, "the entry id is not an instance id");
		});
	}

	[Test]
	public async Task Re_authorizing_keeps_the_newest_entry_and_reports_the_older_one()
	{
		AddAccount("111", "streamer", title: "Twitch (streamer) old", connectedAt: DateTimeOffset.UtcNow.AddDays(-3));
		AddAccount("111", "streamer", title: "Twitch (streamer)", connectedAt: DateTimeOffset.UtcNow);

		await _manager.ReloadAsync(_config);

		var issues = _manager.Issues();
		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections, Has.Count.EqualTo(1), "one Twitch account is one instance");
			Assert.That(_manager.Resolve("111"), Is.Not.Null, "the binding must survive the re-authorization");
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("duplicate-account:111"));
			Assert.That(TestLocalization.Resolve(issues[0].Description), Does.Contain("Twitch (streamer) old"));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel),
				Is.Null,
				"there is no follow-up that can delete an entry");
		});
	}

	[Test]
	public async Task Without_a_timestamp_the_last_entry_in_the_store_wins()
	{
		AddAccount("111", "streamer", title: "first", connectedAt: null);
		AddAccount("111", "streamer", title: "second", connectedAt: null);

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections, Has.Count.EqualTo(1));
			Assert.That(TestLocalization.Resolve(_manager.Issues()[0].Description), Does.Contain("first"));
		});
	}

	[Test]
	public async Task An_incomplete_entry_is_skipped_without_taking_the_others_with_it()
	{
		AddAccount("111", "streamer");
		_config.AddEntry("broken",
			new Dictionary<string, string?>(StringComparer.Ordinal) { [TwitchConfigKeys.ClientId] = "client-id" });

		await _manager.ReloadAsync(_config);

		Assert.That(_manager.Connections.Select(c => c.Account.UserId), Is.EqualTo(_onlyFirstUserId));
	}

	[Test]
	public async Task An_empty_account_id_resolves_to_the_first_account()
	{
		AddAccount("111", "streamer");
		AddAccount("222", "botaccount");

		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Resolve(null)!.Account.UserId, Is.EqualTo("111"));
			Assert.That(_manager.Resolve(string.Empty)!.Account.UserId, Is.EqualTo("111"));
			Assert.That(_manager.Resolve("nonsense"), Is.Null);
		});
	}

	[Test]
	public async Task The_account_picker_offers_every_account_by_user_id()
	{
		AddAccount("111", "streamer", displayName: "ストリーマー");
		AddAccount("222", "botaccount", displayName: "BotAccount");

		await _manager.ReloadAsync(_config);

		var options = _manager.AccountOptions();
		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.Value), Is.EqualTo(_bothUserIds));
			Assert.That(TestLocalization.Resolve(options[0].Label),
				Is.EqualTo("ストリーマー (@streamer)"),
				"a localized display name needs the login beside it to be identifiable");

			Assert.That(TestLocalization.Resolve(options[1].Label), Is.EqualTo("BotAccount"));
		});
	}

	[Test]
	public async Task A_reload_answers_with_no_accounts_when_nothing_is_configured()
	{
		await _manager.ReloadAsync(_config);

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Connections, Is.Empty);
			Assert.That(_manager.Issues(), Is.Empty, "an unconfigured integration has no problem to report");
			Assert.That(_manager.Resolve(null), Is.Null);
		});
	}

	[Test]
	public async Task A_rejected_token_raises_an_issue_naming_the_account()
	{
		var manager = new TwitchAccountManager(() => new RejectingOAuthClient(),
			SilentLogger(),
			(_, _) => new FakeTwitchHelixClient());
		AddAccount("111", "streamer", displayName: "Streamer", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

		await manager.ReloadAsync(_config);
		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() =>
			manager.Connections[0].Tokens.GetAsync(CancellationToken.None));

		var issues = manager.Issues();
		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("token-invalid:111"));
			Assert.That(TestLocalization.Resolve(issues[0].Title), Does.Contain("Streamer"));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.EqualTo("Reconnect"));
		});

		manager.Dispose();
	}

	private Guid AddAccount(
		string userId,
		string login,
		string? displayName = null,
		string? title = null,
		DateTimeOffset? connectedAt = null,
		DateTimeOffset? expiresAt = null)
	{
		var values = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[TwitchConfigKeys.ClientId] = "client-id",
			[TwitchConfigKeys.UserId] = userId,
			[TwitchConfigKeys.Login] = login,
			[TwitchConfigKeys.DisplayName] = displayName ?? login,
			[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
			[TwitchConfigKeys.ExpiresAt] =
				(expiresAt ?? DateTimeOffset.UtcNow.AddHours(4)).ToString("o", CultureInfo.InvariantCulture)
		};

		if (connectedAt is { } stamp)
		{
			values[TwitchConfigKeys.ConnectedAt] = stamp.ToString("o", CultureInfo.InvariantCulture);
		}

		return _config.AddEntry(title ?? $"Twitch ({login})",
			values,
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class RejectingOAuthClient : ITwitchOAuthClient
	{
		public Task<TwitchDeviceCode> RequestDeviceCodeAsync(
			string clientId,
			string scopes,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<TwitchTokenPollResult> PollTokenAsync(
			string clientId,
			string scopes,
			string deviceCode,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<TwitchTokens> RefreshAsync(
			string clientId,
			string refreshToken,
			CancellationToken cancellationToken)
			=> Task.FromException<TwitchTokens>(new TwitchOAuthRejectedException("Invalid refresh token"));

		public Task<TwitchTokenIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public void Dispose()
		{
		}
	}
}
