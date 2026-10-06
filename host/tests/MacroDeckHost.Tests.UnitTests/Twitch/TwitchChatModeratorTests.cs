using System.Globalization;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using TwitchLib.Api.Core.Exceptions;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchChatModeratorTests
{
	private FakeTwitchHelixClient _first = null!;
	private FakeTwitchHelixClient _second = null!;
	private RecordingIntegrationConfig _config = null!;
	private TwitchAccountManager _accounts = null!;
	private TwitchIntegration _integration = null!;

	[SetUp]
	public async Task SetUp()
	{
		_first = new FakeTwitchHelixClient();
		_second = new FakeTwitchHelixClient();
		_config = new RecordingIntegrationConfig();

		AddAccount("111", "streamer");
		AddAccount("222", "second");

		var clients = new Queue<FakeTwitchHelixClient>([_first, _second]);
		_accounts = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			new LoggerConfiguration().CreateLogger(),
			(_, _) => clients.Dequeue());

		await _accounts.ReloadAsync(_config);
		_accounts.Resolve("111")!.Merge(state => state with { IsConnected = true });
		_accounts.Resolve("222")!.Merge(state => state with { IsConnected = true });
		_integration = new TwitchIntegration(_accounts);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
		_accounts.Dispose();
	}

	[Test]
	public async Task Each_action_runs_against_the_chosen_account_as_broadcaster_and_moderator()
	{
		var results = new[]
		{
			await Moderate("222", ChatModerationRequest.Delete("m-1")),
			await Moderate("222", ChatModerationRequest.Timeout("chatter", 600)),
			await Moderate("222", ChatModerationRequest.Ban("chatter")),
			await Moderate("222", ChatModerationRequest.Unban("chatter")),
		};

		Assert.Multiple(() =>
		{
			Assert.That(results, Is.All.EqualTo(ChatModerationResult.Succeeded));
			Assert.That(_second.Calls, Is.EqualTo(new[] { "deleteChat:m-1", "ban:chatter|600|", "ban:chatter||", "unban:chatter" }));
			Assert.That(_first.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_or_unknown_account_is_never_replaced_by_the_first_account()
	{
		var empty = await Moderate(string.Empty, ChatModerationRequest.Ban("chatter"));
		var unknown = await Moderate("999", ChatModerationRequest.Ban("chatter"));

		Assert.Multiple(() =>
		{
			Assert.That(empty, Is.EqualTo(ChatModerationResult.AccountUnavailable));
			Assert.That(unknown, Is.EqualTo(ChatModerationResult.AccountUnavailable));
			Assert.That(_first.Calls.Concat(_second.Calls), Is.Empty);
		});
	}

	[Test]
	public async Task A_disconnected_account_is_unavailable()
	{
		_accounts.Resolve("111")!.Merge(state => state with { IsConnected = false });

		var result = await Moderate("111", ChatModerationRequest.Delete("m-1"));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.AccountUnavailable));
			Assert.That(_first.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Deleting_without_a_message_id_never_clears_the_whole_chat()
	{
		var result = await Moderate("111", ChatModerationRequest.Delete(string.Empty));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.Refused));
			Assert.That(_first.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Twitch_failures_are_reported_as_what_the_user_can_do_about_them()
	{
		_first.FailingWrites["ban"] = new TwitchScopeException("missing scope");
		var missingScope = await Moderate("111", ChatModerationRequest.Ban("chatter"));

		_first.FailingWrites["ban"] = new BadTokenException("forbidden", new HttpResponseMessage());
		var notPermitted = await Moderate("111", ChatModerationRequest.Ban("chatter"));

		_first.FailingWrites["unban"] = new BadRequestException("not banned", new HttpResponseMessage());
		var refused = await Moderate("111", ChatModerationRequest.Unban("chatter"));

		_first.FailingWrites["deleteChat"] = new TwitchRequestException("boom");
		var failed = await Moderate("111", ChatModerationRequest.Delete("m-1"));

		Assert.Multiple(() =>
		{
			Assert.That(missingScope, Is.EqualTo(ChatModerationResult.MissingScope));
			Assert.That(notPermitted, Is.EqualTo(ChatModerationResult.NotPermitted));
			Assert.That(refused, Is.EqualTo(ChatModerationResult.Refused));
			Assert.That(failed, Is.EqualTo(ChatModerationResult.Failed));
		});
	}

	private Task<ChatModerationResult> Moderate(string accountId, ChatModerationRequest request)
		=> ((IStreamChatModerator)_integration).ModerateAsync(accountId, request, CancellationToken.None);

	private void AddAccount(string userId, string login)
		=> _config.AddEntry($"Twitch ({login})",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = userId,
				[TwitchConfigKeys.Login] = login,
				[TwitchConfigKeys.DisplayName] = login,
				[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
				[TwitchConfigKeys.ConnectedAt] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
				[TwitchConfigKeys.ExpiresAt] =
					DateTimeOffset.UtcNow.AddHours(4).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});
}
