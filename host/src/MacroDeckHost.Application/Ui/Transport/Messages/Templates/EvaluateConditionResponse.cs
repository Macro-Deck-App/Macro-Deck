namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class EvaluateConditionResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public bool Result { get; set; }
	public string LeftDisplay { get; set; } = string.Empty;
	public string RightDisplay { get; set; } = string.Empty;
}
