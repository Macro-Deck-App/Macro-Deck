using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetHostSessionRequestMessageHandler
	: IUiTransportMessageHandler<GetHostSessionRequest, GetHostSessionResponse>
{
	private readonly HostSession _session;

	public GetHostSessionRequestMessageHandler(HostSession session) => _session = session;

	public ValueTask<GetHostSessionResponse> Handle(GetHostSessionRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetHostSessionResponse
		{
			SessionId = _session.Id,
			RestoreApplied = _session.RestoreApplied
		});
}
