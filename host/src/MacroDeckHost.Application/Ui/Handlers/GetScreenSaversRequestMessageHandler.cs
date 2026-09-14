using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ScreenSavers;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetScreenSaversRequestMessageHandler
	: IUiTransportMessageHandler<GetScreenSaversRequest, GetScreenSaversResponse>
{
	private readonly IScreenSaverRegistry _registry;

	public GetScreenSaversRequestMessageHandler(IScreenSaverRegistry registry)
	{
		_registry = registry;
	}

	public ValueTask<GetScreenSaversResponse> Handle(
		GetScreenSaversRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetScreenSaversResponse
		{
			ScreenSavers = ScreenSaverDtoMapper.MapToDto(_registry.GetAll())
		});
}
