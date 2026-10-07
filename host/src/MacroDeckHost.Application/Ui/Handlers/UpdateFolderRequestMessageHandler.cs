using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateFolderRequestMessageHandler : IUiTransportMessageHandler<UpdateFolderRequest, UpdateFolderResponse>
{
	private readonly IFolderService _folderService;
	private readonly IColorReferenceResolver _colors;
	private readonly IFolderCache _folders;

	public UpdateFolderRequestMessageHandler(IFolderService folderService,
		IColorReferenceResolver colors,
		IFolderCache folders)
	{
		_colors = colors;
		_folders = folders;
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
		var backgroundColor = ColorSource.Incoming(request.BackgroundColorSource,
			request.BackgroundColor,
			_folders.GetFolderById(id)?.BackgroundColor,
			_colors);

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
			request.FolderViewConfiguration,
			request.EmptyCellStyle);

		var response = new UpdateFolderResponse { Success = result.Success };

		if (result.Success)
		{
			response.Folder = FolderDtoMapper.MapToDto(result.Data!, _colors);
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
