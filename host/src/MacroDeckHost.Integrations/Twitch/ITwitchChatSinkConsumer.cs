using MacroDeckHost.Application.Twitch.Chat;

namespace MacroDeckHost.Integrations.Twitch;

public interface ITwitchChatSinkConsumer
{
	void UseTwitchChatSink(ITwitchChatSink sink);
}
