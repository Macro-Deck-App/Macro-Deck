using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Tests.UnitTests.Variables;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatSinkBindingTests
{
	[Test]
	public async Task The_host_hands_its_chat_to_the_twitch_integration_before_it_starts()
	{
		var sink = new RecordingTwitchChatSink();
		using var integration = new TwitchIntegration();

		IntegrationGatewayBinder.Bind(integration,
			null!,
			null!,
			new InMemoryVariableBindingStore(),
			new VariableRefreshSignal(),
			twitchChatSink: sink);
		await integration.ShutdownAsync();

		Assert.That(sink.AccountLists, Has.Count.EqualTo(1));
	}
}
