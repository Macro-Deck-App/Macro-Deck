using MacroDeckHost.Application.StreamChat;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StreamChatPumpBackgroundService : BackgroundService
{
	private readonly StreamPlatformServices _platforms;

	public StreamChatPumpBackgroundService(StreamPlatformServices platforms)
	{
		_platforms = platforms;
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
		=> Task.WhenAll(_platforms.ChatHubs.Select(hub => hub.RunAsync(stoppingToken)));
}
