using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
internal sealed class EventCascadeNotifierTests
{
	[Test]
	public async Task A_stopped_chain_tells_the_user_once_and_names_the_automation()
	{
		var store = new UserNotificationStore();
		var automations = new StubAutomationCache();
		var automation = automations.Add(null, name: "Ping");
		var notifier
			= new EventCascadeNotifier(store, TestLocalization.Preferences, TestLocalization.Resolver, automations);
		var owner = EventTriggerOwner.ForAutomation(automation.Id);

		await notifier.NotifyStopped(owner, CancellationToken.None);
		await notifier.NotifyStopped(owner, CancellationToken.None);

		var raised = store.Snapshot().Single();
		await Assert.MultipleAsync(() =>
		{
			Assert.That(raised.Title, Is.EqualTo("Event loop stopped"));
			Assert.That(raised.SourceName, Is.EqualTo("Ping"));
			Assert.That(raised.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			return Task.CompletedTask;
		});
	}
}
