using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;

namespace MacroDeckHost.Application.Ui.Handlers;

public class OpenDataDirectoryRequestMessageHandler
	: IUiTransportMessageHandler<OpenDataDirectoryRequest, OpenDataDirectoryResponse>
{
	private readonly IMacroDeckPaths _paths;
	private readonly IFolderRevealService _reveal;

	public OpenDataDirectoryRequestMessageHandler(IMacroDeckPaths paths, IFolderRevealService reveal)
	{
		_paths = paths;
		_reveal = reveal;
	}

	public ValueTask<OpenDataDirectoryResponse> Handle(
		OpenDataDirectoryRequest request,
		CancellationToken cancellationToken)
	{
		var result = _reveal.Reveal(_paths.DataRootDirectory);

		return ValueTask.FromResult(new OpenDataDirectoryResponse
		{
			Success = result.Success,
			Error = result.Success ? null : result.ErrorMessage
		});
	}
}
