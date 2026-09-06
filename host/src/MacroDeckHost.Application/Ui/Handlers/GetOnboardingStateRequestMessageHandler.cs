using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetOnboardingStateRequestMessageHandler
	: IUiTransportMessageHandler<GetOnboardingStateRequest, GetOnboardingStateResponse>
{
	private readonly IAppPreferenceService _service;

	public GetOnboardingStateRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetOnboardingStateResponse> Handle(
		GetOnboardingStateRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetOnboarding();

		return new GetOnboardingStateResponse { Pending = settings.Pending };
	}
}
