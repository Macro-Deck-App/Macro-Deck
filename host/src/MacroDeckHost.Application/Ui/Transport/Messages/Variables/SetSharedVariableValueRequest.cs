namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SetSharedVariableValueRequest
{
	public string Name { get; set; } = string.Empty;
	public string? Value { get; set; }
}
