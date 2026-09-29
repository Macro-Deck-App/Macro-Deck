using System.Text.Json;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatIngestionTests
{
	private const string ChatLine = """
		{
			"broadcaster_user_id": "111",
			"chatter_user_id": "4242",
			"chatter_user_login": "cozyviewer",
			"chatter_user_name": "CozyViewer",
			"message_id": "cc106a89-1814-919d-454c-f4f2f970aae7",
			"message": {
				"text": "Hi chat Kappa 🎉 你好 @streamer",
				"fragments": [
					{ "type": "text", "text": "Hi chat ", "cheermote": null, "emote": null, "mention": null },
					{ "type": "emote", "text": "Kappa", "emote": { "id": "25", "emote_set_id": "0" } },
					{ "type": "text", "text": " 🎉 你好 " },
					{ "type": "mention", "text": "@streamer", "mention": { "user_id": "111" } }
				]
			},
			"color": "#1E90FF",
			"badges": [
				{ "set_id": "moderator", "id": "1", "info": "" },
				{ "set_id": "subscriber", "id": "12", "info": "16" }
			],
			"message_type": "text"
		}
		""";

	private FakeTwitchHelixClient _helix = null!;
	private RecordingTwitchChatSink _sink = null!;
	private RecordingTwitchEventPublisher _events = null!;
	private TwitchAccountManager _manager = null!;
	private TwitchAccountConnection _connection = null!;

	[SetUp]
	public async Task SetUp()
	{
		_helix = new FakeTwitchHelixClient();
		_sink = new RecordingTwitchChatSink();
		_events = new RecordingTwitchEventPublisher();
		_manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(), Logger.None, (_, _) => _helix);

		var config = new RecordingIntegrationConfig();
		TwitchChatTestSupport.AddAccount(config, "111", "streamer");
		_manager.UseChatSink(_sink);
		await _manager.ReloadAsync(config, new TwitchEventEmitter(_events));
		_connection = _manager.Connections[0];
	}

	[TearDown]
	public void TearDown() => _manager.Dispose();

	[Test]
	public void A_chat_line_reaches_the_chat_with_its_emotes_mentions_and_emoji_intact()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("n1", "channel.chat.message", ChatLine));

		var received = _sink.Posted.OfType<TwitchChatMessageReceived>().Single();
		var message = received.Message;

		Assert.Multiple(() =>
		{
			Assert.That(received.AccountId, Is.EqualTo("111"));
			Assert.That(message.MessageId, Is.EqualTo("cc106a89-1814-919d-454c-f4f2f970aae7"));
			Assert.That(message.ChatterName, Is.EqualTo("CozyViewer"));
			Assert.That(message.Color, Is.EqualTo("#1e90ff"));
			Assert.That(message.PlainText, Is.EqualTo("Hi chat Kappa 🎉 你好 @streamer"));
			Assert.That(message.Fragments.Select(f => f.Kind),
				Is.EqualTo(new[]
				{
					TwitchChatFragmentKind.Text, TwitchChatFragmentKind.Emote, TwitchChatFragmentKind.Text,
					TwitchChatFragmentKind.Mention
				}));
			Assert.That(message.Fragments[1].EmoteId, Is.EqualTo("25"));
			Assert.That(message.Badges.Select(b => b.SetId), Is.EqualTo(new[] { "moderator", "subscriber" }));
		});
	}

	[Test]
	public void A_shared_chat_line_names_the_channel_it_came_from()
	{
		var shared = ChatLine.Replace("\"message_type\": \"text\"",
			"""
			"message_type": "text",
			"source_broadcaster_user_id": "777",
			"source_broadcaster_user_login": "partnerchannel",
			"source_broadcaster_user_name": "PartnerChannel"
			""",
			StringComparison.Ordinal);

		_connection.HandleNotification(FakeEventSubClient.Notification("n1", "channel.chat.message", ChatLine));
		_connection.HandleNotification(FakeEventSubClient.Notification("n2", "channel.chat.message",
			shared.Replace("cc106a89", "dd106a89", StringComparison.Ordinal)));

		var messages = _sink.Posted.OfType<TwitchChatMessageReceived>().Select(received => received.Message).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(messages[0].SourceChannelId, Is.Null);
			Assert.That(messages[1].SourceChannelId, Is.EqualTo("777"));
			Assert.That(messages[1].SourceChannelName, Is.EqualTo("PartnerChannel"));
		});
	}

	[Test]
	public void Chat_lines_are_not_automation_events()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("n1", "channel.chat.message", ChatLine));
		_connection.HandleNotification(FakeEventSubClient.Notification("n2",
			"channel.chat.clear_user_messages",
			"""{"target_user_id":"4242"}"""));

		Assert.Multiple(() =>
		{
			Assert.That(_events.Published, Is.Empty);
			Assert.That(_sink.Posted.OfType<TwitchChatUserCleared>().Single().UserId, Is.EqualTo("4242"));
		});
	}

	[Test]
	public void Deleting_a_message_and_clearing_chat_stay_events_and_also_reach_the_chat()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("n1",
			"channel.chat.message_delete",
			"""{"message_id":"abc","target_user_id":"4242"}"""));
		_connection.HandleNotification(FakeEventSubClient.Notification("n2", "channel.chat.clear"));

		Assert.Multiple(() =>
		{
			Assert.That(_events.Published, Does.Contain(TwitchEventIds.ChatMessageDeleted));
			Assert.That(_events.Published, Does.Contain(TwitchEventIds.ChatCleared));
			Assert.That(_sink.Posted.OfType<TwitchChatMessageDeleted>().Single().MessageId, Is.EqualTo("abc"));
			Assert.That(_sink.Posted.OfType<TwitchChatCleared>(), Has.Exactly(1).Items);
		});
	}

	[Test]
	public void A_line_without_a_message_id_is_dropped()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("n1",
			"channel.chat.message",
			"""{"chatter_user_id":"1","message":{"text":"hi"}}"""));

		Assert.That(_sink.Posted, Is.Empty);
	}

	[TestCase("#FF0000", "#ff0000")]
	[TestCase("#abcdef", "#abcdef")]
	public void A_chosen_colour_is_kept_as_lowercase_hex(string sent, string expected)
	{
		var message = Parse(ChatLine.Replace("#1E90FF", sent, StringComparison.Ordinal));

		Assert.That(message.Color, Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("red")]
	[TestCase("#12345")]
	[TestCase("#gggggg")]
	public void A_missing_or_unusable_colour_falls_back_to_the_chatters_default(string sent)
	{
		var message = Parse(ChatLine.Replace("#1E90FF", sent, StringComparison.Ordinal));

		Assert.That(message.Color, Is.EqualTo(TwitchChatStyle.DefaultColor("4242")));
	}

	[Test]
	public async Task Loaded_badges_give_each_badge_its_image_with_channel_badges_winning()
	{
		_helix.GlobalBadges =
		[
			new TwitchChatBadgeImage("moderator", "1", "https://static-cdn.jtvnw.net/badges/v1/mod/2"),
			new TwitchChatBadgeImage("subscriber", "12", "https://static-cdn.jtvnw.net/badges/v1/global-sub/2"),
		];
		_helix.ChannelBadges =
			[new TwitchChatBadgeImage("subscriber", "12", "https://static-cdn.jtvnw.net/badges/v1/channel-sub/2")];

		await _connection.LoadBadgesAsync(CancellationToken.None);
		_connection.HandleNotification(FakeEventSubClient.Notification("n1", "channel.chat.message", ChatLine));

		var badges = _sink.Posted.OfType<TwitchChatMessageReceived>().Single().Message.Badges;
		Assert.That(badges.Select(b => b.ImageUrl),
			Is.EqualTo(new[]
			{
				"https://static-cdn.jtvnw.net/badges/v1/mod/2", "https://static-cdn.jtvnw.net/badges/v1/channel-sub/2"
			}));
	}

	[Test]
	public async Task When_badges_cannot_be_loaded_chat_still_arrives_without_them()
	{
		_helix.FailingReads.Add("globalBadges");

		await _connection.LoadBadgesAsync(CancellationToken.None);
		_connection.HandleNotification(FakeEventSubClient.Notification("n1", "channel.chat.message", ChatLine));

		var message = _sink.Posted.OfType<TwitchChatMessageReceived>().Single().Message;
		Assert.That(message.Badges.Select(b => b.ImageUrl), Is.All.Null);
	}

	[Test]
	public async Task Badges_that_failed_to_load_are_loaded_again_when_the_chat_reconnects()
	{
		_helix.GlobalBadges = [new TwitchChatBadgeImage("moderator", "1", "https://static-cdn.jtvnw.net/badges/v1/mod/2")];
		_helix.FailingReads.Add("globalBadges");
		await _connection.LoadBadgesAsync(CancellationToken.None);
		_helix.FailingReads.Clear();

		_connection.OnConnectionChanged(true);

		var deadline = DateTime.UtcNow.AddSeconds(5);
		TwitchChatMessage message;
		do
		{
			_sink.Posted.Clear();
			_connection.HandleNotification(FakeEventSubClient.Notification(Guid.NewGuid().ToString("N"),
				"channel.chat.message",
				ChatLine));
			message = _sink.Posted.OfType<TwitchChatMessageReceived>().Single().Message;
			if (message.Badges.Any(badge => badge.ImageUrl is not null))
			{
				break;
			}

			await Task.Delay(20);
		}
		while (DateTime.UtcNow < deadline);

		Assert.That(message.Badges.Select(badge => badge.ImageUrl), Does.Contain("https://static-cdn.jtvnw.net/badges/v1/mod/2"));
	}

	private static TwitchChatMessage Parse(string json)
		=> TwitchChatMessageParser.Parse(JsonDocument.Parse(json).RootElement,
			TwitchChatBadgeMap.Empty)!;
}
