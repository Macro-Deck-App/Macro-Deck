using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeck.Sdk.Notifications;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class IntegrationUserNotifierTests
{
	private static readonly string[] _expectedSourceIds = ["integration.a", "integration.b"];
	private static readonly string[] _expectedKeylessTitles = ["Second", "First"];

	[Test]
	public void Notify_namespaces_the_dedupe_key_under_the_integration_id()
	{
		var store = new UserNotificationStore();
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);

		notifier.Notify(new UserNotificationRequest { Title = "Hello", Key = "offline" });

		// Only observable indirectly: raising again under the same request key from a different
		// integration must not collide with (replace) this entry.
		var other = new IntegrationUserNotifier("integration.b", "Integration B", store, Log.Logger);
		other.Notify(new UserNotificationRequest { Title = "Hello from B", Key = "offline" });

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(2), "different integrations must not share a dedupe key");
			Assert.That(notifications.Select(n => n.SourceId), Is.EquivalentTo(_expectedSourceIds));
		});
	}

	[Test]
	public void Dismiss_cannot_touch_another_integrations_key()
	{
		var store = new UserNotificationStore();
		var owner = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);
		var intruder = new IntegrationUserNotifier("integration.b", "Integration B", store, Log.Logger);
		owner.Notify(new UserNotificationRequest { Title = "Hello", Key = "offline" });

		intruder.Dismiss("offline");

		Assert.That(store.Snapshot(),
			Has.Count.EqualTo(1),
			"a same-named key from another integration must not dismiss it");
	}

	[Test]
	public void Notify_twice_under_the_same_key_replaces_rather_than_stacks()
	{
		var store = new UserNotificationStore();
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);

		notifier.Notify(new UserNotificationRequest { Title = "First", Key = "sync-error" });
		notifier.Notify(new UserNotificationRequest { Title = "Second", Key = "sync-error" });

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Title, Is.EqualTo("Second"));
		});
	}

	[Test]
	public void Notifications_without_a_key_stack_rather_than_replacing_each_other()
	{
		var store = new UserNotificationStore();
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);

		notifier.Notify(new UserNotificationRequest { Title = "First" });
		notifier.Notify(new UserNotificationRequest { Title = "Second" });

		Assert.That(store.Snapshot().Select(n => n.Title),
			Is.EqualTo(_expectedKeylessTitles),
			"a missing key must not be namespaced into a shared dedupe key");
	}

	[Test]
	public void Blank_title_is_dropped()
	{
		var store = new UserNotificationStore();
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);

		notifier.Notify(new UserNotificationRequest { Title = "   " });

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public void A_throwing_store_does_not_propagate_out_of_Notify()
	{
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", new ThrowingStore(), Log.Logger);

		Assert.DoesNotThrow(() => notifier.Notify(new UserNotificationRequest { Title = "Hello" }));
	}

	[Test]
	public void A_throwing_store_does_not_propagate_out_of_Dismiss()
	{
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", new ThrowingStore(), Log.Logger);

		Assert.DoesNotThrow(() => notifier.Dismiss("anything"));
	}

	[Test]
	public void Notification_carries_the_integration_as_source_and_stamps_an_open_integration_action()
	{
		var store = new UserNotificationStore();
		var notifier = new IntegrationUserNotifier("integration.a", "Integration A", store, Log.Logger);

		notifier.Notify(new UserNotificationRequest { Title = "Hello", Level = UserNotificationLevel.Warning });

		var notification = store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Kind, Is.EqualTo(UserNotificationKind.Integration));
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notification.SourceId, Is.EqualTo("integration.a"));
			Assert.That(notification.SourceName, Is.EqualTo("Integration A"));
			Assert.That(notification.Action!.Kind, Is.EqualTo(UserNotificationActionKind.OpenIntegration));
			Assert.That(notification.Action!.Target, Is.EqualTo("integration.a"));
		});
	}

	private sealed class ThrowingStore : IUserNotificationStore
	{
		public int Capacity => 100;

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public UserNotification? Raise(UserNotificationDraft draft) => throw new InvalidOperationException("boom");

		public UserNotification? RaiseIfAbsent(UserNotificationDraft draft)
			=> throw new InvalidOperationException("boom");

		public IReadOnlyList<UserNotification> Snapshot() => [];

		public bool Dismiss(string id) => throw new InvalidOperationException("boom");

		public bool DismissByKey(string dedupeKey) => throw new InvalidOperationException("boom");

		public bool DismissAll() => throw new InvalidOperationException("boom");

		public bool Retire(string dedupeKey) => throw new InvalidOperationException("boom");

		public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress)
			=> throw new InvalidOperationException("boom");
	}
}
