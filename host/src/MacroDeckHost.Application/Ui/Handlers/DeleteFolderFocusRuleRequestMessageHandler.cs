using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteFolderFocusRuleRequestMessageHandler
	: IUiTransportMessageHandler<DeleteFolderFocusRuleRequest, DeleteFolderFocusRuleResponse>
{
	private readonly IFolderService _folderService;

	public DeleteFolderFocusRuleRequestMessageHandler(IFolderService folderService)
	{
		_folderService = folderService;
	}

	public async ValueTask<DeleteFolderFocusRuleResponse> Handle(DeleteFolderFocusRuleRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId) || !Guid.TryParse(request.RuleId, out var ruleId))
		{
			return new DeleteFolderFocusRuleResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(FolderError.ValidationError),
					Message = AppStrings.Errors.Folders.FocusRuleIdsRequired()
				}
			};
		}

		var result = await _folderService.DeleteFocusRule(folderId, ruleId);

		var response = new DeleteFolderFocusRuleResponse { Success = result.Success };
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
