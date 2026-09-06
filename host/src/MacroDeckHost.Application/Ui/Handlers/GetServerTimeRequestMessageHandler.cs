using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetServerTimeRequestMessageHandler
	: IUiTransportMessageHandler<GetServerTimeRequest, GetServerTimeResponse>
{
	private readonly TimeProvider _timeProvider;

	public GetServerTimeRequestMessageHandler(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public ValueTask<GetServerTimeResponse> Handle(GetServerTimeRequest request, CancellationToken cancellationToken)
	{
		var response = new GetServerTimeResponse
		{
			UtcMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()
		};

		return ValueTask.FromResult(response);
	}
}
