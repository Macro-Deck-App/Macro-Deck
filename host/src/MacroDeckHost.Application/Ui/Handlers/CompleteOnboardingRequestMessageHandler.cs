using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CompleteOnboardingRequestMessageHandler
	: IUiTransportMessageHandler<CompleteOnboardingRequest, CompleteOnboardingResponse>
{
	private readonly IAppPreferenceService _service;

	public CompleteOnboardingRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<CompleteOnboardingResponse> Handle(
		CompleteOnboardingRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetOnboarding(false);

		return new CompleteOnboardingResponse { Pending = settings.Pending };
	}
}
