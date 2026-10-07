using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	DuplicateFolderRequestMessageHandler : IUiTransportMessageHandler<DuplicateFolderRequest, DuplicateFolderResponse>
{
	private readonly IFolderService _folderService;
	private readonly IColorReferenceResolver _colors;

	public DuplicateFolderRequestMessageHandler(IFolderService folderService, IColorReferenceResolver colors)
	{
		_colors = colors;
		_folderService = folderService;
	}

	public async ValueTask<DuplicateFolderResponse> Handle(
		DuplicateFolderRequest request,
		CancellationToken cancellationToken)
	{
		var folderId = Guid.Parse(request.Id);
		var result = await _folderService.Duplicate(folderId);

		var response = new DuplicateFolderResponse { Success = result.Success };

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
