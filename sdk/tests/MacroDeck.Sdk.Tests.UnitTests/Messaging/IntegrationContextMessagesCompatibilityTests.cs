using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Messaging;

[TestFixture]
public class IntegrationContextMessagesCompatibilityTests
{
	[Test]
	public void A_context_written_before_the_message_channel_existed_still_builds_and_reports_it_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeMessaging();

		var exception = Assert.ThrowsAsync<MessageChannelException>(() => context.Messages.PublishAsync("obs.scene.changed"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.Unsupported));
	}

	private sealed class ContextWrittenBeforeMessaging : IIntegrationContext
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
