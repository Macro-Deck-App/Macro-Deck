using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeChatMappingTests
{
	private const string ChannelId = "UCchannel";

	private RecordingYouTubeChatSink _sink = null!;
	private RecordingYouTubeEventPublisher _events = null!;
	private YouTubeAccountConnection _connection = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new RecordingYouTubeChatSink();
		_events = new RecordingYouTubeEventPublisher();
		_connection = new YouTubeAccountConnection(YouTubeTestSupport.Account(ChannelId, "My Channel"),
			null,
			null,
			new FakeYouTubeApiClient(),
			new YouTubeQuotaBudget(10_000, new YouTubeManualClock(YouTubeTestSupport.Now)),
			new YouTubeEventEmitter(_events),
			YouTubeTestSupport.Silent,
			_sink,
			YouTubeTestSupport.ManualOptions());
	}

	[TearDown]
	public void TearDown() => _connection.Dispose();

	[Test]
	public void A_text_message_is_a_chat_line_by_its_author_in_a_default_colour_and_fires_no_event()
	{
		Receive(YouTubeTestSupport.Text("m1", "hello there", "UCviewer"));

		var line = Line();
		Assert.Multiple(() =>
		{
			Assert.That(line.MessageId, Is.EqualTo("m1"));
			Assert.That(line.AuthorId, Is.EqualTo("UCviewer"));
			Assert.That(line.AuthorLogin, Is.EqualTo("UCviewer"));
			Assert.That(line.AuthorName, Is.EqualTo("Viewer"));
			Assert.That(line.Color, Is.EqualTo(ChatStyle.DefaultColor("UCviewer")));
			Assert.That(line.Badges, Is.Empty);
			Assert.That(line.PlainText, Is.EqualTo("hello there"));
			Assert.That(_events.Published, Is.Empty);
		});
	}

	[Test]
	public void A_super_chat_shows_its_amount_and_fires_super_chat()
	{
		Receive(new YouTubeChatMessage("sc1", YouTubeChatMessageTypes.SuperChat, null, "Love it",
			YouTubeTestSupport.Author("UCfan", "Fan"))
		{
			SuperChat = new YouTubeSuperChat(5_000_000, "USD", "$5.00", "Love it", 2)
		});

		var payload = _events.Single(YouTubeEventIds.SuperChat);
		Assert.Multiple(() =>
		{
			Assert.That(Line().PlainText, Is.EqualTo("$5.00 Love it"));
			Assert.That(payload["account"], Is.EqualTo(ChannelId));
			Assert.That(payload["accountName"], Is.EqualTo("My Channel"));
			Assert.That(payload["messageId"], Is.EqualTo("sc1"));
			Assert.That(payload["authorChannelId"], Is.EqualTo("UCfan"));
			Assert.That(payload["authorName"], Is.EqualTo("Fan"));
			Assert.That(payload["amount"], Is.EqualTo("$5.00"));
			Assert.That(payload["amountMicros"], Is.EqualTo(5_000_000));
			Assert.That(payload["currency"], Is.EqualTo("USD"));
			Assert.That(payload["tier"], Is.EqualTo(2));
			Assert.That(payload["comment"], Is.EqualTo("Love it"));
			Assert.That(_events.Single(YouTubeEventIds.Any)["type"], Is.EqualTo(YouTubeEventIds.SuperChat));
		});
	}

	[Test]
	public void A_super_sticker_shows_its_amount_and_fires_super_sticker()
	{
		Receive(new YouTubeChatMessage("st1", YouTubeChatMessageTypes.SuperSticker, null, null,
			YouTubeTestSupport.Author("UCfan", "Fan"))
		{
			SuperSticker = new YouTubeSuperSticker(2_000_000, "EUR", "€2.00", 1, "sticker-1", "Party parrot")
		});

		var payload = _events.Single(YouTubeEventIds.SuperSticker);
		Assert.Multiple(() =>
		{
			Assert.That(Line().PlainText, Is.EqualTo("€2.00 Party parrot"));
			Assert.That(payload["amount"], Is.EqualTo("€2.00"));
			Assert.That(payload["currency"], Is.EqualTo("EUR"));
			Assert.That(payload["sticker"], Is.EqualTo("Party parrot"));
		});
	}

	[Test]
	public void A_new_member_is_a_chat_line_and_fires_new_member()
	{
		Receive(new YouTubeChatMessage("n1", YouTubeChatMessageTypes.NewSponsor, null, "Welcome to Gold!",
			YouTubeTestSupport.Author("UCnew", "Newbie"))
		{
			NewSponsor = new YouTubeNewSponsor("Gold", IsUpgrade: true)
		});

		var payload = _events.Single(YouTubeEventIds.NewMember);
		Assert.Multiple(() =>
		{
			Assert.That(Line().PlainText, Is.EqualTo("Welcome to Gold!"));
			Assert.That(payload["levelName"], Is.EqualTo("Gold"));
			Assert.That(payload["isUpgrade"], Is.True);
			Assert.That(payload["authorName"], Is.EqualTo("Newbie"));
		});
	}

	[Test]
	public void A_member_milestone_is_a_chat_line_and_fires_member_milestone()
	{
		Receive(new YouTubeChatMessage("ms1", YouTubeChatMessageTypes.MemberMilestone, null, "12 months!",
			YouTubeTestSupport.Author())
		{
			MemberMilestone = new YouTubeMemberMilestone("12 months!", 12, "Gold")
		});

		var payload = _events.Single(YouTubeEventIds.MemberMilestone);
		Assert.Multiple(() =>
		{
			Assert.That(Line().PlainText, Is.EqualTo("12 months!"));
			Assert.That(payload["months"], Is.EqualTo(12));
			Assert.That(payload["levelName"], Is.EqualTo("Gold"));
			Assert.That(payload["comment"], Is.EqualTo("12 months!"));
		});
	}

	[Test]
	public void Gifted_memberships_are_a_chat_line_and_fire_membership_gift()
	{
		Receive(new YouTubeChatMessage("g1", YouTubeChatMessageTypes.MembershipGifting, null,
			"Gifted 5 memberships", YouTubeTestSupport.Author("UCgifter", "Gifter"))
		{
			MembershipGifting = new YouTubeMembershipGifting(5, "Gold")
		});

		var payload = _events.Single(YouTubeEventIds.MembershipGift);
		Assert.Multiple(() =>
		{
			Assert.That(Line().PlainText, Is.EqualTo("Gifted 5 memberships"));
			Assert.That(payload["giftCount"], Is.EqualTo(5));
			Assert.That(payload["levelName"], Is.EqualTo("Gold"));
		});
	}

	[Test]
	public void A_banned_user_clears_their_messages()
	{
		Receive(new YouTubeChatMessage("b1", YouTubeChatMessageTypes.UserBanned, null, null, YouTubeTestSupport.Author())
		{
			UserBanned = new YouTubeUserBanned("UCtroll", "Troll", "permanent", null)
		});

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Posted, Is.EqualTo(new ChatEvent[] { new ChatUserCleared(ChannelId, "UCtroll") }));
			Assert.That(_events.Published, Is.Empty);
		});
	}

	[TestCase(YouTubeChatMessageTypes.MessageDeleted)]
	[TestCase(YouTubeChatMessageTypes.MessageRetracted)]
	public void A_deleted_or_retracted_message_removes_its_target(string type)
	{
		Receive(new YouTubeChatMessage("d1", type, null, null, YouTubeTestSupport.Author())
		{
			DeletedMessageId = "m1"
		});

		Assert.That(_sink.Posted, Is.EqualTo(new ChatEvent[] { new ChatMessageDeleted(ChannelId, "m1") }));
	}

	[Test]
	public void A_tombstone_removes_the_message_it_replaces()
	{
		Receive(new YouTubeChatMessage("m1", YouTubeChatMessageTypes.Tombstone, null, null, YouTubeTestSupport.Author()));

		Assert.That(_sink.Posted, Is.EqualTo(new ChatEvent[] { new ChatMessageDeleted(ChannelId, "m1") }));
	}

	[TestCase(YouTubeChatMessageTypes.GiftMembershipReceived)]
	[TestCase("pollEvent")]
	[TestCase("giftEvent")]
	[TestCase("sponsorOnlyModeStartedEvent")]
	[TestCase("somethingYouTubeAddsLater")]
	public void Other_message_types_are_ignored(string type)
	{
		Receive(new YouTubeChatMessage("x1", type, null, "text", YouTubeTestSupport.Author()));

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Posted, Is.Empty);
			Assert.That(_events.Published, Is.Empty);
		});
	}

	private void Receive(YouTubeChatMessage message) => _connection.OnChatMessages([message], true);

	private ChatMessage Line() => _sink.Posted.OfType<ChatMessageReceived>().Single().Message;
}
