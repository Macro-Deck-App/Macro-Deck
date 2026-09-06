using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

internal sealed class SpotifyContextStub(IIntegrationConfig config) : IIntegrationContext
{
	public IIntegrationConfig Config { get; } = config;
	public IVariableApi Variables => throw new NotSupportedException();
	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IEventPublisher Events => throw new NotSupportedException();
	public IUserNotifier Notifications => throw new NotSupportedException();
}
