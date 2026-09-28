namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SetVariableSharedRequest
{
	public string Id { get; set; } = string.Empty;
	public bool Shared { get; set; }
}
