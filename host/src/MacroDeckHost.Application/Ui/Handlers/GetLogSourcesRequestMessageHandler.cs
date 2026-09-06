using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLogSourcesRequestMessageHandler
	: IUiTransportMessageHandler<GetLogSourcesRequest, GetLogSourcesResponse>
{
	private readonly ILogFileReader _reader;

	public GetLogSourcesRequestMessageHandler(ILogFileReader reader)
	{
		_reader = reader;
	}

	public ValueTask<GetLogSourcesResponse> Handle(GetLogSourcesRequest request, CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetLogSourcesResponse { Sources = _reader.ReadSources().ToList() });
}
