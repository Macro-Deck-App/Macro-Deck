namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class VariableTemplateErrorDto
{
	// An open set: a client that does not know a code shows a generic message.
	public string Code { get; set; } = string.Empty;

	public string? Detail { get; set; }
}
