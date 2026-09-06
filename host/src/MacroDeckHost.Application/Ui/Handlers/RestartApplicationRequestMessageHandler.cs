using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RestartApplicationRequestMessageHandler
	: IUiTransportMessageHandler<RestartApplicationRequest, RestartApplicationResponse>
{
	private readonly IApplicationRestartService _restart;

	public RestartApplicationRequestMessageHandler(IApplicationRestartService restart)
	{
		_restart = restart;
	}

	public ValueTask<RestartApplicationResponse> Handle(
		RestartApplicationRequest request,
		CancellationToken cancellationToken)
	{
		var availability = _restart.Availability;
		var result = _restart.Request(request.Reason ?? "unspecified");

		return ValueTask.FromResult(new RestartApplicationResponse
		{
			Success = result.Success,
			Supported = availability.Supported,
			Error = result.Success ? null : result.ErrorMessage
		});
	}
}
