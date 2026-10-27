using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

[TestFixture]
public class RestoreFailureNotifierTests
{
	private sealed class FakeRestartService : IApplicationRestartService
	{
		public RestartAvailability Availability { get; init; } = new(true, null);

		public bool RestartRequested => false;

		public Result<RestartError> Request(string reason)
		{
			return Result.Ok<RestartError>();
		}
	}

	private static UserNotificationStore Notify(RestoreFailure failure, string culture = "en")
	{
		var session = new HostSession();
		session.MarkRestoreFailed(failure);
		var store = new UserNotificationStore();

		new RestoreFailureNotifier(session,
				new FakeRestartService(),
				store,
				new FakeLocalizationPreferences { Culture = culture },
				TestLocalization.Resolver)
			.NotifyIfFailed()
			.GetAwaiter()
			.GetResult();

		return store;
	}

	[Test]
	public void Nothing_is_raised_after_a_start_without_a_failed_restore()
	{
		Assert.That(Notify(RestoreFailure.None).Snapshot(), Is.Empty);
	}

	[Test]
	public void A_restore_that_was_not_applied_tells_the_user_the_data_is_unchanged()
	{
		var notification = Notify(RestoreFailure.NotApplied).Snapshot().Single();

		Assert.Multiple(() =>
		{
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notification.Title, Is.EqualTo("Restore failed"));
			Assert.That(notification.Message,
				Is.EqualTo("The backup could not be restored. Your data is as it was before the restore."));
			Assert.That(notification.Action, Is.Null);
		});
	}

	[Test]
	public void An_unfinished_rollback_asks_for_a_restart_to_finish_it()
	{
		var notification = Notify(RestoreFailure.RollbackIncomplete).Snapshot().Single();

		Assert.Multiple(() =>
		{
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notification.Message, Does.Contain("Restart Macro Deck"));
			Assert.That(notification.Action?.Kind, Is.EqualTo(UserNotificationActionKind.RestartApplication));
		});
	}

	[Test]
	public void The_notification_is_shown_in_the_users_language()
	{
		var notification = Notify(RestoreFailure.NotApplied, "de").Snapshot().Single();

		Assert.That(notification.Title, Is.EqualTo("Wiederherstellung fehlgeschlagen"));
	}
}
