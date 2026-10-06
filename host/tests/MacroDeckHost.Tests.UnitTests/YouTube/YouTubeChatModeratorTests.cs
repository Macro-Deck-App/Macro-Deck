using System.Net;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.Twitch;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeChatModeratorTests
{
	private const string ChannelId = "UCchannel";

	private FakeYouTubeApiClient _api = null!;
	private RecordingYouTubeChatSink _sink = null!;
	private YouTubeAccountManager _accounts = null!;
	private YouTubeChatModerator _moderator = null!;

	[SetUp]
	public async Task SetUp()
	{
		_api = new FakeYouTubeApiClient();
		_sink = new RecordingYouTubeChatSink();
		var config = new RecordingIntegrationConfig();
		YouTubeTestSupport.AddChannel(config, ChannelId, "Channel");
		_accounts = YouTubeTestSupport.Manager(_ => _api);
		_accounts.UseChatSink(_sink);
		await _accounts.ReloadAsync(config);
		_moderator = new YouTubeChatModerator(() => _accounts);
	}

	[TearDown]
	public void TearDown() => _accounts.Dispose();

	[Test]
	public async Task Nothing_is_moderated_while_the_channel_has_no_live_chat()
	{
		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Delete("m1"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.AccountUnavailable));
			Assert.That(_api.DeletedMessages, Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_account_never_falls_back_to_the_first_channel()
	{
		GoLive();

		var result = await _moderator.ModerateAsync(string.Empty, ChatModerationRequest.Delete("m1"),
			CancellationToken.None);

		Assert.That(result, Is.EqualTo(ChatModerationResult.AccountUnavailable));
	}

	[Test]
	public async Task Deleting_removes_the_message_from_youtube_and_from_the_local_chat()
	{
		GoLive();

		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Delete("m1"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.Succeeded));
			Assert.That(_api.DeletedMessages, Is.EqualTo(new[] { "m1" }));
			Assert.That(_sink.Posted, Has.Member(new ChatMessageDeleted(ChannelId, "m1")));
		});
	}

	[Test]
	public async Task A_timeout_is_a_temporary_ban_for_the_chosen_seconds()
	{
		GoLive();

		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Timeout("UCtroll", 600),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.Succeeded));
			Assert.That(_api.Bans, Is.EqualTo(new[] { ("chat-1", "UCtroll", (long?)600) }));
		});
	}

	[Test]
	public async Task A_ban_placed_here_can_be_lifted_again()
	{
		GoLive();
		_api.NextBanId = "ban-42";

		var banned = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Ban("UCtroll"),
			CancellationToken.None);
		var unbanned = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Unban("UCtroll"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(banned, Is.EqualTo(ChatModerationResult.Succeeded));
			Assert.That(_api.Bans, Is.EqualTo(new[] { ("chat-1", "UCtroll", (long?)null) }));
			Assert.That(unbanned, Is.EqualTo(ChatModerationResult.Succeeded));
			Assert.That(_api.DeletedBans, Is.EqualTo(new[] { "ban-42" }));
		});
	}

	[Test]
	public async Task A_ban_macro_deck_does_not_know_cannot_be_lifted_here()
	{
		GoLive();

		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Unban("UCstranger"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(ChatModerationResult.Unsupported));
			Assert.That(_api.Calls, Has.None.EqualTo("unban"));
		});
	}

	[TestCase("liveChatBanInsertionNotAllowed", ChatModerationResult.Refused)]
	[TestCase("forbidden", ChatModerationResult.Refused)]
	[TestCase("insufficientPermissions", ChatModerationResult.MissingScope)]
	[TestCase("quotaExceeded", ChatModerationResult.Failed)]
	public async Task Youtube_refusals_map_to_the_result_the_dialog_explains(
		string reason,
		ChatModerationResult expected)
	{
		GoLive();
		_api.Failures["ban"] = YouTubeTestSupport.ApiError(reason);

		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Ban("UCowner"),
			CancellationToken.None);

		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_token_youtube_still_refuses_after_refreshing_is_a_missing_permission()
	{
		GoLive();
		_api.Failures["delete"] = YouTubeTestSupport.ApiError("authError", HttpStatusCode.Unauthorized);

		var result = await _moderator.ModerateAsync(ChannelId, ChatModerationRequest.Delete("m1"), CancellationToken.None);

		Assert.That(result, Is.EqualTo(ChatModerationResult.MissingScope));
	}

	private void GoLive()
		=> _accounts.Resolve(ChannelId)!.OnPolled(YouTubeAccountState.Unknown with
		{
			IsLive = true,
			LiveChatId = "chat-1"
		});
}
