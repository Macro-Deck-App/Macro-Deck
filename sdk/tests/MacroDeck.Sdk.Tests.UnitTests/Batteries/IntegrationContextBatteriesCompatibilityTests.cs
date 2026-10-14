using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Batteries;

[TestFixture]
public class IntegrationContextBatteriesCompatibilityTests
{
	[Test]
	public void A_context_written_before_batteries_existed_still_builds_and_reports_no_devices()
	{
		IIntegrationContext context = new ContextWrittenBeforeBatteries();
		var raised = false;

		context.Batteries.Changed += (_, _) => raised = true;

		Assert.Multiple(() =>
		{
			Assert.That(context.Batteries.Devices, Is.Empty);
			Assert.That(context.Batteries.Find("adb:phone"), Is.Null);
			Assert.That(raised, Is.False);
		});
	}

	private sealed class ContextWrittenBeforeBatteries : IIntegrationContext
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
