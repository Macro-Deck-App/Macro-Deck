using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class MoveFolderRequestMessageHandler : IUiTransportMessageHandler<MoveFolderRequest, MoveFolderResponse>
{
	private readonly IFolderService _folderService;

	public MoveFolderRequestMessageHandler(IFolderService folderService)
	{
		_folderService = folderService;
	}

	public async ValueTask<MoveFolderResponse> Handle(MoveFolderRequest request, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id) || !Guid.TryParse(request.TargetId, out var targetId))
		{
			return new MoveFolderResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(FolderError.ValidationError),
					Message = AppStrings.Errors.Folders.MoveIdsRequired()
				}
			};
		}

		if (!Enum.TryParse<FolderMovePosition>(request.Position, ignoreCase: true, out var position))
		{
			return new MoveFolderResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(FolderError.ValidationError),
					Message = AppStrings.Errors.Folders.InvalidMovePosition()
				}
			};
		}

		var result = await _folderService.Move(id, targetId, position);

		var response = new MoveFolderResponse { Success = result.Success };

		if (result.Success)
		{
			response.Folders = result.Data!.Select(FolderDtoMapper.MapToPlacement).ToList();
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
