using MacroDeckHost.Application.Twitch.Stats;

namespace MacroDeckHost.Integrations.Twitch;

public interface ITwitchStatsSinkConsumer
{
	void UseTwitchStatsSink(ITwitchStatsSink sink);
}
