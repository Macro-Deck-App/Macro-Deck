using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateIconRequestMessageHandler : IUiTransportMessageHandler<UpdateIconRequest, UpdateIconResponse>
{
	private readonly IIconService _iconService;

	public UpdateIconRequestMessageHandler(IIconService iconService)
	{
		_iconService = iconService;
	}

	public async ValueTask<UpdateIconResponse> Handle(UpdateIconRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var iconId))
		{
			return new UpdateIconResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.ValidationError),
					Message = AppStrings.Errors.Icons.IconIdRequired()
				}
			};
		}

		var result = await _iconService.Rename(iconId, request.Name);
		var response = new UpdateIconResponse { Success = result.Success };
		if (result.Success)
		{
			response.Icon = IconMapper.ToDto(result.Data!);
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
