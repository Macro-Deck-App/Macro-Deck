using MacroDeckHost.Application.VideoStreams;

namespace MacroDeckHost.Plugins;

public sealed class VideoStreamShutdownService : IHostedService
{
	private static readonly TimeSpan _bound = TimeSpan.FromSeconds(2);

	private readonly IVideoStreamSessionBroker _broker;

	public VideoStreamShutdownService(IVideoStreamSessionBroker broker) => _broker = broker;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => _broker.ShutdownAsync(_bound);
}
