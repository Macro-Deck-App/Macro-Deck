using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateIconPackRequestMessageHandler
	: IUiTransportMessageHandler<CreateIconPackRequest, CreateIconPackResponse>
{
	private readonly IIconPackService _iconPackService;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public CreateIconPackRequestMessageHandler(IIconPackService iconPackService, IIconPackOwnerRegistry ownerRegistry)
	{
		_iconPackService = iconPackService;
		_ownerRegistry = ownerRegistry;
	}

	public async ValueTask<CreateIconPackResponse> Handle(CreateIconPackRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _iconPackService.Create(request.Name,
			request.Description,
			request.Author,
			request.Version);

		var response = new CreateIconPackResponse { Success = result.Success };
		if (result.Success)
		{
			response.Pack = IconMapper.ToDto(result.Data!, 0, _ownerRegistry.Describe(result.Data!));
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
