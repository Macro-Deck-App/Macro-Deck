using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Notifications;

[TestFixture]
public class IntegrationContextDesktopNotificationsCompatibilityTests
{
	[Test]
	public void A_context_written_before_desktop_notifications_existed_still_builds_and_shows_none()
	{
		IIntegrationContext context = new ContextWrittenBeforeDesktopNotifications();
		var desktop = context.DesktopNotifications;

		Assert.Multiple(() =>
		{
			Assert.That(desktop.IsSupported, Is.False);
			Assert.That(desktop.ShowAsync("Hello", "World").IsCompletedSuccessfully, Is.True);
		});
	}

	[Test]
	public void The_unsupported_notifier_still_honours_cancellation_and_rejects_null_text()
	{
		var desktop = UnsupportedDesktopNotifier.Instance;

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<OperationCanceledException>(() =>
				desktop.ShowAsync("Hello", "World", new CancellationToken(true)));
			Assert.ThrowsAsync<ArgumentNullException>(() => desktop.ShowAsync(null!, "World"));
		});
	}

	private sealed class ContextWrittenBeforeDesktopNotifications : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
