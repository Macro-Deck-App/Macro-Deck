namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SanitizeVariableNameResponse
{
	public string Sanitized { get; set; } = string.Empty;
	public bool IsValid { get; set; }
}
