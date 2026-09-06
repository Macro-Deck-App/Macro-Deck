using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class SignOutConnectRequestMessageHandler
	: IUiTransportMessageHandler<SignOutConnectRequest, SignOutConnectResponse>
{
	private readonly IConnectSessionService _sessionService;

	public SignOutConnectRequestMessageHandler(IConnectSessionService sessionService)
		=> _sessionService = sessionService;

	public async ValueTask<SignOutConnectResponse> Handle(SignOutConnectRequest request,
		CancellationToken cancellationToken)
	{
		await _sessionService.SignOut(cancellationToken);
		return new SignOutConnectResponse();
	}
}
