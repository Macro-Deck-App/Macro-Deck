using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class StartConnectSignInRequestMessageHandler
	: IUiTransportMessageHandler<StartConnectSignInRequest, StartConnectSignInResponse>
{
	private readonly IConnectSessionService _sessionService;

	public StartConnectSignInRequestMessageHandler(IConnectSessionService sessionService)
		=> _sessionService = sessionService;

	public async ValueTask<StartConnectSignInResponse> Handle(StartConnectSignInRequest request,
		CancellationToken cancellationToken)
	{
		var start = await _sessionService.StartSignIn(cancellationToken);

		return new StartConnectSignInResponse
		{
			VerificationUriComplete = start.VerificationUriComplete.ToString(),
			VerificationUri = start.VerificationUri.ToString(),
			UserCode = start.UserCode,
			ExpiresAtUtc = start.ExpiresAtUtc
		};
	}
}
