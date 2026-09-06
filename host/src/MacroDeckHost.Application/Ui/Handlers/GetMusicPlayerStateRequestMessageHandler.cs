using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetMusicPlayerStateRequestMessageHandler
	: IUiTransportMessageHandler<GetMusicPlayerStateRequest, GetMusicPlayerStateResponse>
{
	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerStateCache _cache;

	public GetMusicPlayerStateRequestMessageHandler(IMusicPlayerRegistry registry, IMusicPlayerStateCache cache)
	{
		_registry = registry;
		_cache = cache;
	}

	public ValueTask<GetMusicPlayerStateResponse> Handle(
		GetMusicPlayerStateRequest request,
		CancellationToken cancellationToken)
	{
		var instanceId = request.InstanceId;
		if (instanceId is null)
		{
			var instances = _registry.GetInstances();
			instanceId = instances.Count > 0 ? instances[0].InstanceId : null;
		}

		var player = instanceId is null ? null : _registry.GetPlayer(instanceId);
		if (instanceId is null || player is null)
		{
			return ValueTask.FromResult(new GetMusicPlayerStateResponse
			{
				State = MusicPlayerStatePayload.Disconnected(instanceId)
			});
		}

		return ValueTask.FromResult(new GetMusicPlayerStateResponse { State = _cache.GetState(instanceId) });
	}
}
