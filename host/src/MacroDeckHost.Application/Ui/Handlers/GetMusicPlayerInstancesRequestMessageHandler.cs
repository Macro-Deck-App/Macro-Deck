using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetMusicPlayerInstancesRequestMessageHandler
	: IUiTransportMessageHandler<GetMusicPlayerInstancesRequest, GetMusicPlayerInstancesResponse>
{
	private readonly IMusicPlayerInstancesSnapshot _snapshot;

	public GetMusicPlayerInstancesRequestMessageHandler(IMusicPlayerInstancesSnapshot snapshot)
	{
		_snapshot = snapshot;
	}

	public ValueTask<GetMusicPlayerInstancesResponse> Handle(
		GetMusicPlayerInstancesRequest request,
		CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(new GetMusicPlayerInstancesResponse
		{
			Instances = _snapshot.Instances.ToList()
		});
	}
}
