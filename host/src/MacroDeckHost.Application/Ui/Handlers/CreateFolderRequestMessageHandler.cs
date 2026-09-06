using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateFolderRequestMessageHandler : IUiTransportMessageHandler<CreateFolderRequest, CreateFolderResponse>
{
	private readonly IFolderService _folderService;

	public CreateFolderRequestMessageHandler(IFolderService folderService)
	{
		_folderService = folderService;
	}

	public async ValueTask<CreateFolderResponse> Handle(
		CreateFolderRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.ProfileId, out var profileId))
		{
			return new CreateFolderResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(FolderError.ValidationError),
					Message = AppStrings.Errors.Folders.ProfileIdRequired()
				}
			};
		}

		Guid? parentId = string.IsNullOrEmpty(request.ParentId) ? null : Guid.Parse(request.ParentId);

		var result = await _folderService.Create(profileId,
			request.Name,
			parentId,
			request.FolderViewId,
			request.FolderViewConfiguration);

		var response = new CreateFolderResponse { Success = result.Success };

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
