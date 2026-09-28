using MacroDeckHost.Application.Twitch.Chat;

namespace MacroDeckHost.Integrations.Twitch;

// The Twitch integration is created without dependency injection, so the host hands it the chat hub this
// way before it initializes.
public interface ITwitchChatSinkConsumer
{
	void UseTwitchChatSink(ITwitchChatSink sink);
}
