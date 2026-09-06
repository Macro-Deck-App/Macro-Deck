using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateFolderRequestMessageHandler : IUiTransportMessageHandler<UpdateFolderRequest, UpdateFolderResponse>
{
	private readonly IFolderService _folderService;

	public UpdateFolderRequestMessageHandler(IFolderService folderService)
	{
		_folderService = folderService;
	}

	public async ValueTask<UpdateFolderResponse> Handle(
		UpdateFolderRequest request,
		CancellationToken cancellationToken)
	{
		var id = Guid.Parse(request.Id);
		var parentId = request.ParentId != null ? (Guid?)Guid.Parse(request.ParentId) : null;
		var order = request.Order;
		var rows = request.Rows;
		var columns = request.Columns;
		var name = request.Name;
		var backgroundColor = request.BackgroundColor;

		var result = await _folderService.Update(id,
			name,
			parentId,
			order,
			rows,
			columns,
			backgroundColor,
			request.WidgetSpacing,
			request.WidgetBorderRadius,
			request.IsDefault,
			request.FolderViewId,
			request.FolderViewConfiguration);

		var response = new UpdateFolderResponse { Success = result.Success };

		if (result.Success)
		{
			response.Folder = FolderDtoMapper.MapToDto(result.Data!);
		}
		else
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
