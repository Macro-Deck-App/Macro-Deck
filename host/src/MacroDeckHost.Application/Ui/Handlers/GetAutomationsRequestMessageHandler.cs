using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetAutomationsRequestMessageHandler
	: IUiTransportMessageHandler<GetAutomationsRequest, GetAutomationsResponse>
{
	private readonly IAutomationService _automationService;
	private readonly StartupReadiness _readiness;

	public GetAutomationsRequestMessageHandler(IAutomationService automationService, StartupReadiness readiness)
	{
		_automationService = automationService;
		_readiness = readiness;
	}

	public async ValueTask<GetAutomationsResponse> Handle(
		GetAutomationsRequest request,
		CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not observe the still-empty
		// automation cache as "no automations".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		return new GetAutomationsResponse
		{
			Automations = _automationService.GetAll().Select(AutomationDtoMapper.ToDto).ToList()
		};
	}
}
