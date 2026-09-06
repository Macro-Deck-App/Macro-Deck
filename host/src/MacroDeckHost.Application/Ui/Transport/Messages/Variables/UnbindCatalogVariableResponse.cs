namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class UnbindCatalogVariableResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
