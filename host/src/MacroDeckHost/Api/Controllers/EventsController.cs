using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetEventDefinitionsRequest, GetEventDefinitionsResponse>
		_getDefinitions;

	private readonly IUiTransportMessageHandler<TriggerEventRequest, TriggerEventResponse> _trigger;

	public EventsController(
		IUiTransportMessageHandler<GetEventDefinitionsRequest, GetEventDefinitionsResponse> getDefinitions,
		IUiTransportMessageHandler<TriggerEventRequest, TriggerEventResponse> trigger)
	{
		_getDefinitions = getDefinitions;
		_trigger = trigger;
	}

	[HttpGet]
	public Task<GetEventDefinitionsResponse> GetAll(CancellationToken ct)
		=> _getDefinitions.Handle(new GetEventDefinitionsRequest(), ct).AsTask();

	[HttpPost("trigger")]
	public Task<TriggerEventResponse> Trigger(TriggerEventRequest body, CancellationToken ct)
		=> _trigger.Handle(body, ct).AsTask();
}
