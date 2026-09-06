using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

[TestFixture]
public class PersistenceRecoveryNotifierTests
{
	private static PersistenceRecoveryNotifier CreateNotifier(UserNotificationStore store)
		=> new(store, TestLocalization.ScopeFactory, TestLocalization.Resolver);

	[Test]
	public void ReportRecovered_RaisesOneCoalescedWarning_ForSeveralRecoveredDocuments()
	{
		var store = new UserNotificationStore();
		var notifier = CreateNotifier(store);

		notifier.ReportRecovered(new PersistenceRecovery("profile", "/a.json", PersistenceRecoverySource.Backup, null));
		notifier.ReportRecovered(new PersistenceRecovery("profile", "/b.json", PersistenceRecoverySource.Backup, null));
		notifier.ReportRecovered(new PersistenceRecovery("profile",
			"/c.json",
			PersistenceRecoverySource.PendingWrite,
			null));

		var notifications = store.Snapshot();
		Assert.That(notifications, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Message, Does.Contain("3"));
		});
	}

	[Test]
	public void ReportRecovered_DoesNotCountTheSameDocumentTwice()
	{
		var store = new UserNotificationStore();
		var notifier = CreateNotifier(store);

		notifier.ReportRecovered(new PersistenceRecovery("profile", "/a.json", PersistenceRecoverySource.Backup, null));
		notifier.ReportRecovered(new PersistenceRecovery("profile", "/a.json", PersistenceRecoverySource.Backup, null));

		var notifications = store.Snapshot();
		Assert.That(notifications, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(notifications[0].Message, Does.Contain("1 file"));
			Assert.That(notifications[0].Message, Does.Not.Contain("2 file"));
		});
	}

	[Test]
	public void ReportUnrecoverable_RaisesASeparateError_AlongsideTheRecoveryWarning()
	{
		var store = new UserNotificationStore();
		var notifier = CreateNotifier(store);

		notifier.ReportRecovered(new PersistenceRecovery("profile", "/a.json", PersistenceRecoverySource.Backup, null));
		notifier.ReportRecovered(new PersistenceRecovery("profile", "/b.json", PersistenceRecoverySource.Backup, null));
		notifier.ReportUnrecoverable(new PersistenceLoss("icon pack", "/pack.json"));

		var notifications = store.Snapshot();
		Assert.That(notifications, Has.Count.EqualTo(2));
		var warning = notifications.Single(n => n.Severity == UserNotificationSeverity.Warning);
		var error = notifications.Single(n => n.Severity == UserNotificationSeverity.Error);
		Assert.Multiple(() =>
		{
			Assert.That(warning.Message, Does.Contain("2"));
			Assert.That(error.Message, Does.Contain("1 file"));
		});
	}

	[Test]
	public void ReportRecovered_IsWrittenInTheLanguageTheRestOfTheInterfaceUses()
	{
		var store = new UserNotificationStore();
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences { Culture = "de" });
		var notifier = new PersistenceRecoveryNotifier(store,
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			TestLocalization.Resolver);

		notifier.ReportRecovered(new PersistenceRecovery("profile", "/a.json", PersistenceRecoverySource.Backup, null));

		var notification = store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Title,
				Is.EqualTo("Dateien nach einem abgebrochenen Schreibvorgang wiederhergestellt"));
			Assert.That(notification.Message, Does.Contain("1 Datei"));
		});
	}

	[Test]
	public void ReportRecovered_IsNeverReached_WhenEveryProfileLoadsCleanly()
	{
		var notificationStore = new UserNotificationStore();
		var recoveryNotifier = CreateNotifier(notificationStore);
		var paths = new TestPaths();
		try
		{
			var profileStore = new JsonProfileStore(paths, new LoggerConfiguration().CreateLogger(), recoveryNotifier);
			profileStore.Save(new ProfileFile { Id = Guid.NewGuid(), Name = "V1" });
			profileStore.Save(new ProfileFile { Id = Guid.NewGuid(), Name = "V2" });

			profileStore.LoadAll();

			Assert.That(notificationStore.Snapshot(), Is.Empty);
		}
		finally
		{
			paths.Cleanup();
		}
	}
}
