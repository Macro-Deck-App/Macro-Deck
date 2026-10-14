using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxCacheTests
{
	[Test]
	public async Task Inboxes_of_every_provider_are_merged_under_inbox_refs_with_counts_and_messages()
	{
		var imap = new FakeEmailIntegration("app.imap", "IMAP").WithInbox("work", "Work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("a", 1), Message("b", 2, true)], 40));
		var plugin = new FakeEmailIntegration("com.example.mail").WithInbox("home", "Home");
		using var world = new EmailWorld(imap, plugin);

		await world.SyncAllAsync();

		var inboxes = world.Cache.Snapshot.Inboxes;
		var work = world.Cache.Snapshot.Find("app.imap::work")!;
		Assert.Multiple(() =>
		{
			Assert.That(inboxes.Select(state => state.Ref),
				Is.EqualTo(new[] { "com.example.mail::home", "app.imap::work" }),
				"ordered by name");
			Assert.That(work.Slug, Is.EqualTo("work"));
			Assert.That(work.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That((work.UnreadCount, work.TotalCount), Is.EqualTo((1, 40)));
			Assert.That(work.Latest.Select(message => message.Id), Is.EqualTo(new[] { "b", "a" }));
			Assert.That(work.LatestUnread.Select(message => message.Id), Is.EqualTo(new[] { "a" }));
			Assert.That(TestLocalization.Resolve(inboxes[0].Inbox.ProviderName),
				Is.EqualTo("com.example.mail integration"));
			Assert.That(world.Events.Changes, Is.Empty, "the first read is a baseline");
		});
	}

	[Test]
	public async Task Unread_mail_older_than_the_newest_messages_is_read_with_an_unread_only_query()
	{
		var messages = Enumerable.Range(1, 30)
			.Select(minute => Message("m" + minute, minute, minute > 1))
			.ToList();
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, messages));
		using var world = new EmailWorld(imap);

		await world.SyncAllAsync();

		var work = world.Cache.Snapshot.Inboxes.Single();
		Assert.Multiple(() =>
		{
			Assert.That(work.Latest, Has.Count.EqualTo(EmailInboxCache.WindowSize));
			Assert.That(work.LatestUnread.Select(message => message.Id), Is.EqualTo(new[] { "m1" }));
			Assert.That(imap.Reads.Select(read => read.Query.UnreadOnly), Is.EqualTo(new[] { false, true }));
		});
	}

	[Test]
	public async Task Changed_is_raised_only_when_something_actually_changed()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		using var world = new EmailWorld(imap);
		var raised = 0;
		world.Cache.Changed += () => raised++;

		await world.SyncAllAsync();
		await world.SyncAllAsync();
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("a", 1)]));
		await world.SyncAllAsync();

		Assert.That(raised, Is.EqualTo(2));
	}

	[Test]
	public async Task A_changed_inbox_is_read_again_without_reading_the_others()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work").WithInbox("home");
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();
		imap.Reads.Clear();

		world.Signal.Request("app.imap", "work");
		await world.SyncPendingAsync();

		Assert.That(imap.Reads.Select(read => read.InboxId).Distinct(), Is.EqualTo(new[] { "work" }));
	}

	[Test]
	public async Task A_failed_read_keeps_the_last_data_as_stale_and_retries_once_after_thirty_seconds()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("a", 1)]));
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();

		imap.Failing.Add("work");
		await world.SyncAllAsync();
		var afterFirstFailure = world.Cache.Snapshot.Inboxes.Single();
		var retryAt = world.Cache.NextRetryAt;

		world.Time.Advance(EmailInboxCache.RetryDelay);
		await world.SyncPendingAsync();
		var afterSecondFailure = world.Cache.Snapshot.Inboxes.Single();

		Assert.Multiple(() =>
		{
			Assert.That(afterFirstFailure.IsStale, Is.True);
			Assert.That(afterFirstFailure.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That(afterFirstFailure.UnreadCount, Is.EqualTo(1));
			Assert.That(afterFirstFailure.Latest.Single().Id, Is.EqualTo("a"));
			Assert.That(retryAt, Is.EqualTo(Noon + EmailInboxCache.RetryDelay));
			Assert.That(afterSecondFailure.IsStale, Is.True);
			Assert.That(afterSecondFailure.Status, Is.EqualTo(EmailInboxStatus.Disconnected));
			Assert.That(afterSecondFailure.Latest.Single().Id, Is.EqualTo("a"));
			Assert.That(world.Cache.NextRetryAt, Is.Null, "only one retry");
		});
	}

	[Test]
	public async Task A_provider_that_stops_answering_disconnects_its_inbox_once()
	{
		var plugin = new FakeEmailIntegration("com.example.mail").WithInbox("work");
		using var world = new EmailWorld(plugin);
		await world.SyncAllAsync();

		plugin.Failing.Add("work");
		for (var pass = 0; pass < 4; pass++)
		{
			await world.SyncAllAsync();
			world.Time.Advance(EmailInboxCache.RetryDelay);
		}

		Assert.Multiple(() =>
		{
			Assert.That(world.Events.Changes.OfType<EmailInboxDisconnected>().Count(), Is.EqualTo(1));
			Assert.That(world.Events.Changes.OfType<EmailInboxConnected>(), Is.Empty);
			Assert.That(world.Cache.Snapshot.Inboxes.Single().Status, Is.EqualTo(EmailInboxStatus.Disconnected));
		});
	}

	[Test]
	public async Task The_status_a_provider_reports_wins_and_its_return_fires_connected()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();

		imap.Set("work", Snapshot(EmailInboxStatus.Error, 0, []) with { StatusMessage = "Sign-in failed." });
		await world.SyncAllAsync();
		var failed = world.Cache.Snapshot.Inboxes.Single();
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 0, []));
		await world.SyncAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(failed.Status, Is.EqualTo(EmailInboxStatus.Error));
			Assert.That(failed.StatusMessage, Is.EqualTo("Sign-in failed."));
			Assert.That(failed.IsStale, Is.False);
			Assert.That(world.Events.Changes.Select(change => change.GetType()),
				Is.EqualTo(new[] { typeof(EmailInboxDisconnected), typeof(EmailInboxConnected) }));
		});
	}

	[Test]
	public async Task New_mail_after_the_baseline_is_published_as_received()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 0, [Message("a", 1, true)]));
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();

		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("a", 1, true), Message("b", 2)]));
		imap.RaiseChanged("work");
		await world.SyncPendingAsync();

		Assert.That(world.Events.Changes.Select(change => change.GetType()),
			Is.EqualTo(new[] { typeof(EmailReceived), typeof(EmailUnreadCountChanged) }));
	}

	[Test]
	public async Task Existing_mail_behind_a_connecting_first_read_is_not_published_as_received()
	{
		var plugin = new FakeEmailIntegration("com.example.mail").WithInbox("work");
		plugin.Set("work",
			new EmailInboxSnapshot
			{
				Status = EmailInboxStatus.Connecting, UnreadCount = 0, TotalCount = 0, SyncedAt = default
			});
		using var world = new EmailWorld(plugin);
		await world.SyncAllAsync();

		plugin.Set("work", Snapshot(EmailInboxStatus.Connected, 2, [Message("a", 1), Message("b", 2)]));
		plugin.RaiseChanged("work");
		await world.SyncPendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(world.Cache.Snapshot.Inboxes.Single().UnreadCount, Is.EqualTo(2));
			Assert.That(world.Events.Changes, Is.Empty);
		});
	}

	[Test]
	public async Task Failed_reads_keep_the_baseline_so_only_mail_that_arrived_meanwhile_fires()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 0, [Message("a", 1, true)]));
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();

		imap.Failing.Add("work");
		await world.SyncAllAsync();
		await world.SyncAllAsync();
		imap.Failing.Remove("work");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("a", 1, true), Message("b", 2)]));
		await world.SyncAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(world.Events.Changes.Select(change => change.GetType()),
				Is.EqualTo(new[]
				{
					typeof(EmailInboxDisconnected), typeof(EmailInboxConnected), typeof(EmailReceived),
					typeof(EmailUnreadCountChanged)
				}));
			Assert.That(world.Events.Changes.OfType<EmailReceived>().Single().Message.Id, Is.EqualTo("b"));
		});
	}

	[Test]
	public async Task An_inbox_that_disappears_is_dropped_with_its_provider()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work").WithInbox("home");
		using var world = new EmailWorld(imap);
		await world.SyncAllAsync();

		imap.Inboxes.RemoveAll(inbox => inbox.Id == "home");
		await world.SyncAllAsync();

		Assert.That(world.Cache.Snapshot.Inboxes.Select(state => state.Ref), Is.EqualTo(new[] { "app.imap::work" }));
	}

	[Test]
	public async Task The_aggregate_sums_the_selected_inboxes_and_merges_their_messages_newest_first()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work").WithInbox("home");
		imap.Set("work", Snapshot(EmailInboxStatus.Connected, 2, [Message("w1", 1), Message("w2", 3)]));
		imap.Set("home", Snapshot(EmailInboxStatus.Connected, 1, [Message("h1", 2)]));
		var other = new FakeEmailIntegration("com.example.mail").WithInbox("spam");
		other.Set("spam", Snapshot(EmailInboxStatus.Connected, 9, [Message("s1", 4)]));
		using var world = new EmailWorld(imap, other);
		await world.SyncAllAsync();

		var selected = world.Cache.Aggregate(["app.imap::work", "app.imap::home"]);
		var all = world.Cache.Aggregate([]);

		Assert.Multiple(() =>
		{
			Assert.That(selected.UnreadCount, Is.EqualTo(3));
			Assert.That(selected.Latest.Select(entry => entry.Message.Id), Is.EqualTo(new[] { "w2", "h1", "w1" }));
			Assert.That(all.UnreadCount, Is.EqualTo(12), "an empty selection means every inbox");
		});
	}
}
