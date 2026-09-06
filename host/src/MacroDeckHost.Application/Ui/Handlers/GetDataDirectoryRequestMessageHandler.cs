using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetDataDirectoryRequestMessageHandler
	: IUiTransportMessageHandler<GetDataDirectoryRequest, GetDataDirectoryResponse>
{
	private readonly IMacroDeckPaths _paths;

	public GetDataDirectoryRequestMessageHandler(IMacroDeckPaths paths)
	{
		_paths = paths;
	}

	public ValueTask<GetDataDirectoryResponse> Handle(
		GetDataDirectoryRequest request,
		CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(new GetDataDirectoryResponse
		{
			Path = _paths.DataRootDirectory,
			CanOpen = request.TrustedConnection
		});
	}
}
