using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Notifications;

[TestFixture]
public class IntegrationContextNotificationServiceCompatibilityTests
{
	[Test]
	public void A_context_written_before_the_notification_service_existed_still_builds_and_reports_it_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeNotificationService();

		var show = Assert.ThrowsAsync<NotificationException>(() =>
			context.NotificationService.ShowAsync(new NotificationOptions { Title = "Hello" }));
		var ask = Assert.ThrowsAsync<NotificationException>(() =>
			context.NotificationService.ShowForResultAsync(new NotificationOptions { Title = "Hello" }));

		Assert.Multiple(() =>
		{
			Assert.That(show!.ErrorCode, Is.EqualTo(NotificationErrorCode.Unsupported));
			Assert.That(ask!.ErrorCode, Is.EqualTo(NotificationErrorCode.Unsupported));
		});
	}

	[Test]
	public void The_fire_and_forget_notifier_is_unchanged_on_such_a_context()
	{
		IIntegrationContext context = new ContextWrittenBeforeNotificationService();

		Assert.That(() => context.Notifications.Notify(new UserNotificationRequest { Title = "Still works" }),
			Throws.Nothing);
	}

	private sealed class ContextWrittenBeforeNotificationService : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications { get; } = new SilentNotifier();
	}

	private sealed class SilentNotifier : IUserNotifier
	{
		public void Notify(UserNotificationRequest notification)
		{
		}

		public void Dismiss(string key)
		{
		}
	}
}
