using System.Diagnostics;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatHubTests
{
	private const string Streamer = "111";
	private const string Bot = "222";

	private FakeTimeProvider _time = null!;
	private StreamChatHub _hub = null!;
	private List<string?> _changes = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_hub = new StreamChatHub(_time, Logger.None);
		_changes = [];
		_hub.Changed += (_, args) => _changes.Add(args.AccountId);
		_hub.SetAccounts([new ChatAccount(Streamer, "Streamer"), new ChatAccount(Bot, "Bot")]);
		_hub.Post(new ChatConnectionChanged(Streamer, true));
		_hub.Post(new ChatConnectionChanged(Bot, true));
		_hub.Tick();
		_changes.Clear();
	}

	[TearDown]
	public void TearDown() => _hub.Dispose();

	[Test]
	public void A_burst_in_one_tick_raises_one_change_per_account_and_keeps_only_the_newest_hundred()
	{
		for (var index = 0; index < 10_000; index++)
		{
			_hub.Post(new ChatMessageReceived(index % 2 == 0 ? Streamer : Bot, Message("m" + index)));
		}

		_hub.Tick();

		var streamer = _hub.Snapshot(Streamer).Messages;
		Assert.Multiple(() =>
		{
			Assert.That(_changes, Is.EquivalentTo(new[] { Streamer, Bot }));
			Assert.That(streamer, Has.Count.EqualTo(StreamChatHub.HistoryLimit));
			Assert.That(_hub.Snapshot(Bot).Messages, Has.Count.EqualTo(StreamChatHub.HistoryLimit));
			Assert.That(streamer[^1].MessageId, Is.EqualTo("m9998"), "the newest line is last");
			Assert.That(streamer[0].MessageId, Is.EqualTo("m9800"), "the oldest lines are the ones dropped");
		});
	}

	[Test]
	public void Posting_never_waits_even_when_nobody_drains_the_queue()
	{
		var watch = Stopwatch.StartNew();

		for (var index = 0; index < 50_000; index++)
		{
			_hub.Post(new ChatMessageReceived(Streamer, Message("m" + index)));
		}

		Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public void Moderation_arriving_behind_a_full_queue_of_chat_is_still_applied()
	{
		_hub.Post(new ChatMessageReceived(Streamer, Message("keep")));
		_hub.Tick();

		for (var index = 0; index < 20_000; index++)
		{
			_hub.Post(new ChatMessageReceived(Bot, Message("flood" + index)));
		}

		_hub.Post(new ChatCleared(Streamer));
		_hub.Tick();

		Assert.That(_hub.Snapshot(Streamer).Messages, Is.Empty);
	}

	[Test]
	public void The_same_message_twice_is_shown_once()
	{
		_hub.Post(new ChatMessageReceived(Streamer, Message("dup")));
		_hub.Post(new ChatMessageReceived(Streamer, Message("dup")));
		_hub.Tick();
		_hub.Post(new ChatMessageReceived(Streamer, Message("dup")));
		_hub.Tick();

		Assert.That(_hub.Snapshot(Streamer).Messages.Select(m => m.MessageId), Is.EqualTo(new[] { "dup" }));
	}

	[Test]
	public void A_deleted_message_goes_and_its_neighbours_stay()
	{
		Post(Streamer, "a", "b", "c");
		_hub.Tick();

		_hub.Post(new ChatMessageDeleted(Streamer, "b"));
		_hub.Tick();

		Assert.That(_hub.Snapshot(Streamer).Messages.Select(m => m.MessageId), Is.EqualTo(new[] { "a", "c" }));
	}

	[Test]
	public void A_delete_posted_after_its_message_in_the_same_tick_removes_it()
	{
		_hub.Post(new ChatMessageReceived(Streamer, Message("late")));
		_hub.Post(new ChatMessageDeleted(Streamer, "late"));
		_hub.Tick();

		Assert.That(_hub.Snapshot(Streamer).Messages, Is.Empty);
	}

	[Test]
	public void Clearing_the_chat_empties_only_that_account()
	{
		Post(Streamer, "a");
		Post(Bot, "b");
		_hub.Tick();

		_hub.Post(new ChatCleared(Streamer));
		_hub.Tick();

		Assert.Multiple(() =>
		{
			Assert.That(_hub.Snapshot(Streamer).Messages, Is.Empty);
			Assert.That(_hub.Snapshot(Bot).Messages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Clearing_a_user_removes_only_their_messages()
	{
		_hub.Post(new ChatMessageReceived(Streamer, Message("a", chatterId: "troll")));
		_hub.Post(new ChatMessageReceived(Streamer, Message("b", chatterId: "fan")));
		_hub.Post(new ChatMessageReceived(Streamer, Message("c", chatterId: "troll")));
		_hub.Tick();

		_hub.Post(new ChatUserCleared(Streamer, "troll"));
		_hub.Tick();

		Assert.That(_hub.Snapshot(Streamer).Messages.Select(m => m.MessageId), Is.EqualTo(new[] { "b" }));
	}

	[Test]
	public void An_empty_account_choice_follows_the_first_account()
	{
		Post(Streamer, "a");
		_hub.Tick();

		Assert.Multiple(() =>
		{
			Assert.That(_hub.Snapshot(null).Account?.AccountId, Is.EqualTo(Streamer));
			Assert.That(_hub.Snapshot(string.Empty).Messages, Has.Count.EqualTo(1));
			Assert.That(_hub.Snapshot("unknown"), Is.SameAs(ChatSnapshot.None));
		});
	}

	[Test]
	public void Removing_every_account_leaves_nothing_to_show_and_says_so()
	{
		Post(Streamer, "a");
		_hub.Tick();
		_changes.Clear();

		_hub.SetAccounts([]);
		_hub.Tick();

		Assert.Multiple(() =>
		{
			Assert.That(_changes, Is.EqualTo(new string?[] { null }));
			Assert.That(_hub.Snapshot(Streamer).Account, Is.Null);
		});
	}

	[Test]
	public void A_quiet_tick_raises_nothing()
	{
		_hub.Tick();

		Assert.That(_changes, Is.Empty);
	}

	[Test]
	public async Task The_running_pump_applies_posts_on_its_own_tick()
	{
		using var cancellation = new CancellationTokenSource();
		var run = _hub.RunAsync(cancellation.Token);

		Post(Streamer, "a");
		_time.Advance(TimeSpan.FromMilliseconds(250));

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (_hub.Snapshot(Streamer).Messages.Count == 0 && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		await cancellation.CancelAsync();
		await run;

		Assert.That(_hub.Snapshot(Streamer).Messages, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_resolved_image_refreshes_the_account_showing_it()
	{
		var images = new FakeTwitchChatImages();
		using var hub = new StreamChatHub(_time, Logger.None, images);
		var changes = new List<string?>();
		hub.Changed += (_, args) => changes.Add(args.AccountId);
		hub.SetAccounts([new ChatAccount(Streamer, "Streamer")]);
		hub.Post(new ChatMessageReceived(Streamer, Message("a", emoteId: "25")));
		hub.Tick();
		changes.Clear();

		images.Resolve(TwitchChatImage.Emote("25"));
		hub.Tick();

		Assert.Multiple(() =>
		{
			Assert.That(images.Requested, Does.Contain(TwitchChatImage.Emote("25")));
			Assert.That(images.Pinned, Does.Contain(TwitchChatImage.Emote("25").Key));
			Assert.That(changes, Is.EqualTo(new[] { Streamer }));
		});
	}

	[Test]
	public void Nothing_is_downloaded_while_no_widget_shows_the_chat_and_the_retained_images_are_once_one_does()
	{
		var images = new FakeTwitchChatImages();
		using var hub = new StreamChatHub(_time, Logger.None, images);
		hub.SetAccounts([new ChatAccount(Streamer, "Streamer")]);
		hub.Post(new ChatMessageReceived(Streamer, Message("a", emoteId: "25")));
		hub.Tick();
		var requestedUnwatched = images.Requested.Count;

		hub.Changed += (_, _) => { };
		hub.Tick();

		Assert.Multiple(() =>
		{
			Assert.That(requestedUnwatched, Is.Zero);
			Assert.That(images.Requested, Does.Contain(TwitchChatImage.Emote("25")));
		});
	}

	[Test]
	public void A_message_pushed_out_of_the_history_in_the_same_tick_downloads_nothing()
	{
		var images = new FakeTwitchChatImages();
		using var hub = new StreamChatHub(_time, Logger.None, images);
		hub.Changed += (_, _) => { };
		hub.SetAccounts([new ChatAccount(Streamer, "Streamer")]);
		hub.Tick();

		hub.Post(new ChatMessageReceived(Streamer, Message("old", emoteId: "1")));
		for (var index = 0; index < StreamChatHub.HistoryLimit; index++)
		{
			hub.Post(new ChatMessageReceived(Streamer, Message($"m{index}")));
		}

		hub.Tick();

		Assert.That(images.Requested, Does.Not.Contain(TwitchChatImage.Emote("1")));
	}

	private void Post(string accountId, params string[] ids)
	{
		foreach (var id in ids)
		{
			_hub.Post(new ChatMessageReceived(accountId, Message(id)));
		}
	}

	internal static ChatMessage Message(string id, string chatterId = "42", string? emoteId = null)
		=> new(id,
			chatterId,
			"viewer",
			"Viewer",
			"#1e90ff",
			[],
			emoteId is null
				? [new ChatFragment(ChatFragmentKind.Text, "hello")]
				:
				[
					new ChatFragment(ChatFragmentKind.Text, "hello "),
					new ChatFragment(ChatFragmentKind.Emote, "Kappa", emoteId),
				]);
}
