namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SetVariableValueRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Value { get; set; }
}
