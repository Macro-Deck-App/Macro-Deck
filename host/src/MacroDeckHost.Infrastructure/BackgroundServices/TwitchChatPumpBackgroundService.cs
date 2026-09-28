using MacroDeckHost.Application.Twitch.Chat;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class TwitchChatPumpBackgroundService : BackgroundService
{
	private readonly TwitchChatHub _hub;

	public TwitchChatPumpBackgroundService(TwitchChatHub hub)
	{
		_hub = hub;
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken) => _hub.RunAsync(stoppingToken);
}
