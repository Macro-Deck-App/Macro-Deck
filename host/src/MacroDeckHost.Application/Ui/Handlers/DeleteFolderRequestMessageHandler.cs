using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteFolderRequestMessageHandler : IUiTransportMessageHandler<DeleteFolderRequest, DeleteFolderResponse>
{
	private readonly IFolderService _folderService;

	public DeleteFolderRequestMessageHandler(IFolderService folderService)
	{
		_folderService = folderService;
	}

	public async ValueTask<DeleteFolderResponse> Handle(
		DeleteFolderRequest request,
		CancellationToken cancellationToken)
	{
		var id = Guid.Parse(request.Id);

		var result = await _folderService.Delete(id);

		var response = new DeleteFolderResponse { Success = result.Success };

		if (!result.Success)
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}
}
