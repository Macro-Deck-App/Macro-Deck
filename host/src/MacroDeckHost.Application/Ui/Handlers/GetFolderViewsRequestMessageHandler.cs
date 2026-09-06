using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetFolderViewsRequestMessageHandler
	: IUiTransportMessageHandler<GetFolderViewsRequest, GetFolderViewsResponse>
{
	private readonly IFolderViewRegistry _registry;

	public GetFolderViewsRequestMessageHandler(IFolderViewRegistry registry)
	{
		_registry = registry;
	}

	public ValueTask<GetFolderViewsResponse> Handle(
		GetFolderViewsRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetFolderViewsResponse
		{
			FolderViews = FolderViewDtoMapper.MapToDto(_registry.GetAll())
		});
}
