using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteIconPackRequestMessageHandler
	: IUiTransportMessageHandler<DeleteIconPackRequest, DeleteIconPackResponse>
{
	private readonly IIconPackService _iconPackService;

	public DeleteIconPackRequestMessageHandler(IIconPackService iconPackService)
	{
		_iconPackService = iconPackService;
	}

	public async ValueTask<DeleteIconPackResponse> Handle(DeleteIconPackRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var packId))
		{
			return new DeleteIconPackResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconPackError.ValidationError),
					Message = AppStrings.Errors.Icons.PackIdRequired()
				}
			};
		}

		var result = await _iconPackService.Delete(packId);
		var response = new DeleteIconPackResponse { Success = result.Success };
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
