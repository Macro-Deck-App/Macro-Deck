using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class ReportUpdateStateRequestMessageHandlerTests
{
	private static ReportUpdateStateRequestMessageHandler CreateHandler(IUserNotificationStore store)
		=> new(store, TestLocalization.ScopeFactory, TestLocalization.Resolver);

	[Test]
	public async Task Reporting_an_available_update_raises_one_entry_under_the_update_key()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Kind, Is.EqualTo(UserNotificationKind.Update));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Info));
			Assert.That(notifications[0].Title, Is.EqualTo("Macro Deck 1.2.3 is available"));
		});
	}

	[Test]
	public async Task The_same_version_reported_available_twice_does_not_re_timestamp_the_entry()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		var first = store.Snapshot()[0];

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		var second = store.Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(second, Has.Count.EqualTo(1));
			Assert.That(second[0].Id, Is.EqualTo(first.Id));
			Assert.That(second[0].Timestamp, Is.EqualTo(first.Timestamp));
		});
	}

	[Test]
	public async Task A_newer_version_reported_available_replaces_the_entry_and_notifies_again()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		var first = store.Snapshot()[0];

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.3.0", Phase = "available" },
			CancellationToken.None);
		var notifications = store.Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1), "never two update rows in the center at once");
			Assert.That(notifications[0].Title, Is.EqualTo("Macro Deck 1.3.0 is available"));
			Assert.That(notifications[0].Timestamp, Is.GreaterThanOrEqualTo(first.Timestamp));
		});
	}

	[Test]
	public async Task Available_without_canInstall_offers_details_but_not_install()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(
			new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = false },
			CancellationToken.None);

		var actions = store.Snapshot()[0].Actions.Select(a => a.Kind).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(actions, Does.Contain(UserNotificationActionKind.OpenUpdateDetails));
			Assert.That(actions, Does.Not.Contain(UserNotificationActionKind.InstallUpdate));
		});
	}

	[Test]
	public async Task Available_with_canInstall_offers_install_alongside_details()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);

		var actions = store.Snapshot()[0].Actions.Select(a => a.Kind).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(actions, Does.Contain(UserNotificationActionKind.OpenUpdateDetails));
			Assert.That(actions, Does.Contain(UserNotificationActionKind.InstallUpdate));
			Assert.That(actions, Does.Contain(UserNotificationActionKind.DismissNotification));
		});
	}

	[Test]
	public async Task Successive_downloading_reports_keep_the_entrys_timestamp_and_position()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = "unrelated"
		});

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 0 },
			CancellationToken.None);
		var snapshotAfterFirst = store.Snapshot();
		var updateEntry = snapshotAfterFirst.Single(n => n.Title.Contains("1.2.3"));
		var indexBefore = snapshotAfterFirst.ToList().FindIndex(n => n.Id == updateEntry.Id);

		for (var percent = 10; percent <= 100; percent += 10)
		{
			await handler.Handle(new ReportUpdateStateRequest
					{ Version = "1.2.3", Phase = "downloading", Percent = percent },
				CancellationToken.None);
		}

		var snapshotAfter = store.Snapshot();
		var indexAfter = snapshotAfter.ToList().FindIndex(n => n.Id == updateEntry.Id);

		Assert.Multiple(() =>
		{
			Assert.That(snapshotAfter, Has.Count.EqualTo(2), "no duplicate rows from re-raising per tick");
			Assert.That(indexAfter, Is.EqualTo(indexBefore), "a re-raise per tick would move the entry to the front");
			Assert.That(snapshotAfter.Single(n => n.Id == updateEntry.Id).Timestamp, Is.EqualTo(updateEntry.Timestamp));
			Assert.That(snapshotAfter.Single(n => n.Id == updateEntry.Id).Progress,
				Is.EqualTo(new UserNotificationProgress(100, 100)));
		});
	}

	[Test]
	public async Task An_update_entry_carrying_progress_cannot_be_dismissed()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 10 },
			CancellationToken.None);
		var entry = store.Snapshot()[0];

		Assert.Multiple(() =>
		{
			Assert.That(store.Dismiss(entry.Id), Is.False);
			Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_cancelled_report_unsticks_the_progress_bearing_entry_it_leaves_no_trace_of()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 10 },
			CancellationToken.None);
		var entry = store.Snapshot()[0];
		Assert.That(store.Dismiss(entry.Id), Is.False, "sanity: Dismiss alone could not have removed this entry");

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "cancelled" },
			CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Cancelling_a_download_clears_the_entry_and_a_fresh_available_report_raises_a_new_one()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 40 },
			CancellationToken.None);
		var downloading = store.Snapshot().Single(n => n.Title.Contains("1.2.3"));
		Assert.That(store.Dismiss(downloading.Id), Is.False, "sanity: progress still attached");

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "cancelled" },
			CancellationToken.None);
		Assert.That(store.Snapshot(), Is.Empty, "the cancelled entry must be gone, progress or not");

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications,
				Has.Count.EqualTo(1),
				"the same version must be able to raise a fresh entry after a cancel");
			Assert.That(store.Dismiss(notifications[0].Id),
				Is.True,
				"the fresh entry must be dismissable - no leftover progress from the cancelled download");
		});
	}

	[Test]
	public async Task Downloaded_replaces_the_downloading_entry_with_a_ready_to_install_one()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 50 },
			CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloaded" },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Title, Is.EqualTo("Macro Deck 1.2.3 is ready to install"));
			Assert.That(notifications[0].Progress, Is.Null);
			Assert.That(notifications[0].Actions.Select(a => a.Kind),
				Does.Contain(UserNotificationActionKind.InstallUpdate));
		});
	}

	[Test]
	public async Task A_failed_report_retires_the_running_entry_before_raising_a_failure_notification()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "downloading", Percent = 50 },
			CancellationToken.None);

		var longError = new string('x', 5000);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "failed", Error = longError },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Progress, Is.Null);
			Assert.That(notifications[0].Message,
				Has.Length.LessThan(longError.Length),
				"a multi-KB error must be clamped, never passed through untouched");
		});
	}

	[Test]
	public void The_request_has_no_notes_field_a_changelog_can_never_reach_Message_through_it()
	{
		var properties = typeof(ReportUpdateStateRequest).GetProperties().Select(p => p.Name);

		Assert.That(properties, Does.Not.Contain("Notes"));
	}

	[Test]
	public async Task UpToDate_clears_any_existing_update_entry()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = null, Phase = "upToDate" },
			CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_failed_check_without_a_version_warns_that_updates_could_not_be_checked()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(CheckFailed(), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Kind, Is.EqualTo(UserNotificationKind.Update));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Title, Is.EqualTo("Could not check for updates"));
			Assert.That(notifications[0].Message, Is.EqualTo(CheckFailed().Error));
		});
	}

	[Test]
	public async Task Checks_that_keep_failing_leave_the_first_warning_in_place()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(CheckFailed(), CancellationToken.None);
		var first = store.Snapshot().Single();
		await handler.Handle(CheckFailed(), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Id, Is.EqualTo(first.Id));
			Assert.That(notifications[0].Timestamp, Is.EqualTo(first.Timestamp));
		});
	}

	[Test]
	public async Task A_dismissed_check_failure_stays_dismissed_while_checks_keep_failing()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(CheckFailed(), CancellationToken.None);
		store.Dismiss(store.Snapshot().Single().Id);
		await handler.Handle(CheckFailed(), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_check_that_fails_again_after_one_succeeded_warns_again()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(CheckFailed(), CancellationToken.None);
		store.Dismiss(store.Snapshot().Single().Id);
		await handler.Handle(new ReportUpdateStateRequest { Version = null, Phase = "upToDate" },
			CancellationToken.None);
		await handler.Handle(CheckFailed(), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Title, Is.EqualTo("Could not check for updates"));
		});
	}

	[Test]
	public async Task A_check_that_fails_after_an_update_was_found_leaves_that_update_on_offer()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);
		await handler.Handle(CheckFailed("1.2.3"), CancellationToken.None);
		await handler.Handle(CheckFailed("1.2.3"), CancellationToken.None);

		var titles = store.Snapshot().Select(n => n.Title);
		Assert.That(titles,
			Is.EquivalentTo(new[] { "Macro Deck 1.2.3 is available", "Could not check for updates" }));
	}

	[Test]
	public async Task A_successful_check_clears_the_check_failure_warning()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(CheckFailed(), CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);

		Assert.That(store.Snapshot().Select(n => n.Title), Is.EqualTo(new[] { "Macro Deck 1.2.3 is available" }));
	}

	[Test]
	public async Task A_failed_install_names_the_version_even_when_the_failure_kind_is_reported()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(
			new ReportUpdateStateRequest { Version = "1.2.3", Phase = "failed", Failure = "install", Error = "reset" },
			CancellationToken.None);

		Assert.That(store.Snapshot().Single().Title,
			Is.EqualTo("The update to Macro Deck 1.2.3 could not be completed"));
	}

	[Test]
	public async Task Finding_the_same_update_again_after_a_failed_check_clears_the_warning_and_keeps_the_offer()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);
		var offer = store.Snapshot().Single();
		await handler.Handle(CheckFailed("1.2.3"), CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications.Select(n => n.Title), Is.EqualTo(new[] { "Macro Deck 1.2.3 is available" }));
			Assert.That(notifications[0].Timestamp, Is.EqualTo(offer.Timestamp));
		});
	}

	[Test]
	public async Task A_failure_streak_that_ended_with_the_same_update_found_again_warns_on_the_next_one()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		await handler.Handle(CheckFailed("1.2.3"), CancellationToken.None);
		await handler.Handle(new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);
		await handler.Handle(CheckFailed("1.2.3"), CancellationToken.None);

		Assert.That(store.Snapshot().Select(n => n.Title), Does.Contain("Could not check for updates"));
	}

	private static ReportUpdateStateRequest CheckFailed(string? knownVersion = null)
		=> new()
		{
			Version = knownVersion,
			Phase = "failed",
			Failure = "check",
			Error = "could not check for updates on any feed (https://updates.example/stable.json: connection refused)"
		};
}
