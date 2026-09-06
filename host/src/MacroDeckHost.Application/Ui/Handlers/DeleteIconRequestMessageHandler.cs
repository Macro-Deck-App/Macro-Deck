using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteIconRequestMessageHandler : IUiTransportMessageHandler<DeleteIconRequest, DeleteIconResponse>
{
	private readonly IIconService _iconService;

	public DeleteIconRequestMessageHandler(IIconService iconService)
	{
		_iconService = iconService;
	}

	public async ValueTask<DeleteIconResponse> Handle(DeleteIconRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var iconId))
		{
			return new DeleteIconResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.ValidationError),
					Message = AppStrings.Errors.Icons.IconIdRequired()
				}
			};
		}

		var result = await _iconService.Delete(iconId);
		var response = new DeleteIconResponse { Success = result.Success };
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
