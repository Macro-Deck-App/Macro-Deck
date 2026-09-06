namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class GetActionParameterOptionsResponse
{
	public List<ActionParameterOptionDto> Options { get; set; } = new();

	public bool AllowsCustomValue { get; set; }

	public int? CacheSeconds { get; set; }

	public TransportError? Error { get; set; }
}
