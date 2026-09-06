using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteIconsRequestMessageHandler : IUiTransportMessageHandler<DeleteIconsRequest, DeleteIconsResponse>
{
	private readonly IIconService _iconService;

	public DeleteIconsRequestMessageHandler(IIconService iconService)
	{
		_iconService = iconService;
	}

	public async ValueTask<DeleteIconsResponse> Handle(DeleteIconsRequest request,
		CancellationToken cancellationToken)
	{
		var iconIds = new List<Guid>();
		foreach (var id in request.Ids)
		{
			if (!Guid.TryParse(id, out var iconId))
			{
				return new DeleteIconsResponse
				{
					Success = false,
					Error = new TransportError
					{
						Code = nameof(IconError.ValidationError),
						Message = AppStrings.Errors.Icons.AllIconIdsMustBeValid()
					}
				};
			}

			iconIds.Add(iconId);
		}

		var result = await _iconService.DeleteMany(iconIds);
		var response = new DeleteIconsResponse { Success = result.Success };
		if (result.Success)
		{
			response.DeletedCount = result.Data;
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
