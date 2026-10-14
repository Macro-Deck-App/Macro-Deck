using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailChangeDetectorTests
{
	private static readonly EmailInboxDescriptor _inbox
		= new("app.imap::work", "app.imap", "work", "IMAP", "Work", "me@example.com");

	[Test]
	public void The_first_read_of_an_inbox_is_a_baseline_and_reports_nothing()
	{
		var detection = EmailChangeDetector.Detect(null,
			State(EmailInboxStatus.Connected, 2, Message("a", 1), Message("b", 2)),
			null);

		Assert.That(detection.Changes, Is.Empty);
	}

	[Test]
	public void A_new_message_on_top_of_the_window_is_received_once()
	{
		var before = State(EmailInboxStatus.Connected, 1, Message("a", 1, true), Message("b", 2));
		var after = State(EmailInboxStatus.Connected,
			2,
			Message("a", 1, true),
			Message("b", 2),
			Message("c", 3));

		var first = EmailChangeDetector.Detect(before, after, true);
		var again = EmailChangeDetector.Detect(first.Baseline, after, true);

		Assert.Multiple(() =>
		{
			Assert.That(first.Changes.OfType<EmailReceived>().Select(change => change.Message.Id),
				Is.EqualTo(new[] { "c" }));
			Assert.That(again.Changes, Is.Empty, "a message already seen does not fire again");
		});
	}

	[Test]
	public void Mail_that_arrived_during_an_outage_fires_once_when_the_inbox_is_read_again()
	{
		var before = State(EmailInboxStatus.Connected, 0, Message("a", 1, true));
		var stale = before with { IsStale = true };
		var after = State(EmailInboxStatus.Connected,
			2,
			Message("a", 1, true),
			Message("b", 30),
			Message("c", 40));

		var duringOutage = EmailChangeDetector.Detect(before, stale, true);
		var afterOutage = EmailChangeDetector.Detect(duringOutage.Baseline, after, true);

		Assert.Multiple(() =>
		{
			Assert.That(duringOutage.Changes, Is.Empty);
			Assert.That(afterOutage.Changes.OfType<EmailReceived>().Select(change => change.Message.Id),
				Is.EqualTo(new[] { "b", "c" }),
				"oldest first");
		});
	}

	[Test]
	public void New_ids_for_old_mail_after_a_mailbox_reset_fire_nothing()
	{
		var before = State(EmailInboxStatus.Connected, 0, Message("1-1", 1, true), Message("1-2", 2, true));
		var after = State(EmailInboxStatus.Connected, 0, Message("2-1", 1, true), Message("2-2", 2, true));

		var detection = EmailChangeDetector.Detect(before, after, true);

		Assert.That(detection.Changes.OfType<EmailReceived>(), Is.Empty);
	}

	[Test]
	public void A_flood_larger_than_the_window_still_fires_every_new_message_it_shows()
	{
		var before = State(EmailInboxStatus.Connected, 0, Message("old", 1, true));
		var flood = Enumerable.Range(10, EmailInboxCache.WindowSize)
			.Select(minute => Message("new-" + minute, minute))
			.ToArray();
		var after = State(EmailInboxStatus.Connected, 25, flood);

		var detection = EmailChangeDetector.Detect(before, after, true);

		Assert.That(detection.Changes.OfType<EmailReceived>().Count(), Is.EqualTo(EmailInboxCache.WindowSize));
	}

	[Test]
	public void An_older_message_sliding_into_the_window_after_a_deletion_is_not_received()
	{
		var before = State(EmailInboxStatus.Connected, 0, Message("b", 2, true), Message("c", 3, true));
		var after = State(EmailInboxStatus.Connected, 0, Message("a", 1, true), Message("b", 2, true));

		var detection = EmailChangeDetector.Detect(before, after, true);

		Assert.That(detection.Changes, Is.Empty);
	}

	[Test]
	public void A_read_flip_and_the_unread_count_are_reported()
	{
		var before = State(EmailInboxStatus.Connected, 2, Message("a", 1), Message("b", 2));
		var after = State(EmailInboxStatus.Connected, 1, Message("a", 1, true), Message("b", 2));

		var detection = EmailChangeDetector.Detect(before, after, true);

		Assert.Multiple(() =>
		{
			var flip = detection.Changes.OfType<EmailReadStateChanged>().Single();
			Assert.That(flip.Message.Id, Is.EqualTo("a"));
			Assert.That(flip.Message.IsRead, Is.True);

			var unread = detection.Changes.OfType<EmailUnreadCountChanged>().Single();
			Assert.That((unread.PreviousUnreadCount, unread.UnreadCount), Is.EqualTo((2, 1)));
			Assert.That(detection.Changes.OfType<EmailReceived>(), Is.Empty);
		});
	}

	[Test]
	public void Starting_up_through_connecting_to_connected_is_silent_about_mail_that_already_exists()
	{
		var connecting = State(EmailInboxStatus.Connecting, 0);
		var connected = State(EmailInboxStatus.Connected, 2, Message("a", 1), Message("b", 2));

		var first = EmailChangeDetector.Detect(null, connecting, null);
		var second = EmailChangeDetector.Detect(first.Baseline, connected, first.Connected);

		Assert.Multiple(() =>
		{
			Assert.That(first.Changes, Is.Empty);
			Assert.That(second.Changes, Is.Empty, "the first connected read is the baseline");
			Assert.That(second.Connected, Is.True);
		});
	}

	[Test]
	public void A_provider_reconnecting_with_no_data_in_between_is_silent()
	{
		var connected = State(EmailInboxStatus.Connected, 1, Message("a", 1), Message("b", 2, true));
		var connecting = State(EmailInboxStatus.Connecting, 0);

		var baseline = EmailChangeDetector.Detect(null, connected, null);
		var gap = EmailChangeDetector.Detect(baseline.Baseline, connecting, baseline.Connected);
		var back = EmailChangeDetector.Detect(gap.Baseline, connected, gap.Connected);

		Assert.Multiple(() =>
		{
			Assert.That(gap.Changes, Is.Empty);
			Assert.That(back.Changes, Is.Empty, "the mail was already there before the gap");
		});
	}

	[Test]
	public void Mail_that_arrived_while_the_provider_reported_no_data_fires_once()
	{
		var before = State(EmailInboxStatus.Connected, 0, Message("a", 1, true));
		var disconnected = State(EmailInboxStatus.Disconnected, 0);
		var after = State(EmailInboxStatus.Connected, 1, Message("a", 1, true), Message("b", 30));

		var baseline = EmailChangeDetector.Detect(null, before, null);
		var gap = EmailChangeDetector.Detect(baseline.Baseline, disconnected, baseline.Connected);
		var back = EmailChangeDetector.Detect(gap.Baseline, after, gap.Connected);
		var again = EmailChangeDetector.Detect(back.Baseline, after, back.Connected);

		Assert.Multiple(() =>
		{
			Assert.That(gap.Changes.Single(),
				Is.TypeOf<EmailInboxDisconnected>(),
				"an empty disconnected read reports no mail changes");
			Assert.That(back.Changes.OfType<EmailReceived>().Select(change => change.Message.Id),
				Is.EqualTo(new[] { "b" }));
			Assert.That(back.Changes.OfType<EmailUnreadCountChanged>().Single().UnreadCount, Is.EqualTo(1));
			Assert.That(again.Changes, Is.Empty);
		});
	}

	[Test]
	public void Losing_and_regaining_the_connection_fires_once_each_way()
	{
		var connected = State(EmailInboxStatus.Connected, 0);
		var error = State(EmailInboxStatus.Error, 0);
		var reconnecting = State(EmailInboxStatus.Connecting, 0);

		var lost = EmailChangeDetector.Detect(connected, error, true);
		var stillLost = EmailChangeDetector.Detect(error, reconnecting, lost.Connected);
		var back = EmailChangeDetector.Detect(reconnecting, connected, stillLost.Connected);

		Assert.Multiple(() =>
		{
			Assert.That(lost.Changes.Single(), Is.TypeOf<EmailInboxDisconnected>());
			Assert.That(stillLost.Changes, Is.Empty, "connecting is neither connected nor disconnected");
			Assert.That(back.Changes.Single(), Is.TypeOf<EmailInboxConnected>());
		});
	}

	private static EmailInboxState State(EmailInboxStatus status, int unreadCount, params EmailMessage[] messages)
	{
		return new EmailInboxState
		{
			Inbox = _inbox,
			Slug = "work",
			Status = status,
			UnreadCount = unreadCount,
			TotalCount = messages.Length,
			Latest = [.. messages.OrderByDescending(message => message.ReceivedAt)],
			SyncedAt = Noon
		};
	}
}
