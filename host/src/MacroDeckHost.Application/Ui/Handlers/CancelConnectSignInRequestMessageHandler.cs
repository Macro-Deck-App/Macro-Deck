using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CancelConnectSignInRequestMessageHandler
	: IUiTransportMessageHandler<CancelConnectSignInRequest, CancelConnectSignInResponse>
{
	private readonly IConnectSessionService _sessionService;

	public CancelConnectSignInRequestMessageHandler(IConnectSessionService sessionService)
		=> _sessionService = sessionService;

	public async ValueTask<CancelConnectSignInResponse> Handle(CancelConnectSignInRequest request,
		CancellationToken cancellationToken)
	{
		await _sessionService.CancelSignIn(cancellationToken);
		return new CancelConnectSignInResponse();
	}
}
