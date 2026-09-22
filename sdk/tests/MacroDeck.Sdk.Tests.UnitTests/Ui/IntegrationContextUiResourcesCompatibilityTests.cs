using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Ui;

[TestFixture]
public class IntegrationContextUiResourcesCompatibilityTests
{
	[Test]
	public void A_context_written_before_ui_resources_existed_still_builds_and_reports_them_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeUiResources();

		var exception = Assert.ThrowsAsync<UiResourceException>(
			() => context.UiResources.RegisterAsync("photo", new byte[] { 1 }, "image/png"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public void Removing_through_a_context_written_before_ui_resources_existed_reports_them_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeUiResources();

		var exception = Assert.ThrowsAsync<UiResourceException>(() => context.UiResources.RemoveAsync("photo"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	private sealed class ContextWrittenBeforeUiResources : IIntegrationContext
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
