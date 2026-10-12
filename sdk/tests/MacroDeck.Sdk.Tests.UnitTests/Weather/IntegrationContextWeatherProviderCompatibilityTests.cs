using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Weather;

[TestFixture]
public class IntegrationContextWeatherProviderCompatibilityTests
{
	[Test]
	public void A_context_written_before_weather_notices_existed_still_builds_and_ignores_them()
	{
		IIntegrationContext context = new ContextWrittenBeforeWeatherNotices();

		Assert.DoesNotThrowAsync(() => context.WeatherProvider.NotifySnapshotsChangedAsync());
	}

	private sealed class ContextWrittenBeforeWeatherNotices : IIntegrationContext
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
