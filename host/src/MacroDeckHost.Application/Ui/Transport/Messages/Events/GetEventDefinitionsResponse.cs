namespace MacroDeckHost.Application.Ui.Transport.Messages.Events;

public class GetEventDefinitionsResponse
{
	public List<EventDefinitionDto> Events { get; set; } = [];
}
