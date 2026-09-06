using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateIconPackRequestMessageHandler
	: IUiTransportMessageHandler<UpdateIconPackRequest, UpdateIconPackResponse>
{
	private readonly IIconPackService _iconPackService;
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public UpdateIconPackRequestMessageHandler(IIconPackService iconPackService,
		IIconPackCache iconPackCache,
		IIconPackOwnerRegistry ownerRegistry)
	{
		_iconPackService = iconPackService;
		_iconPackCache = iconPackCache;
		_ownerRegistry = ownerRegistry;
	}

	public async ValueTask<UpdateIconPackResponse> Handle(UpdateIconPackRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var packId))
		{
			return new UpdateIconPackResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconPackError.ValidationError),
					Message = AppStrings.Errors.Icons.PackIdRequired()
				}
			};
		}

		var result = await _iconPackService.Update(packId,
			request.Name,
			request.Description,
			request.Author,
			request.Version);

		var response = new UpdateIconPackResponse { Success = result.Success };
		if (result.Success)
		{
			response.Pack = IconMapper.ToDto(result.Data!,
				_iconPackCache.GetIconCount(packId),
				_ownerRegistry.Describe(result.Data!));
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
