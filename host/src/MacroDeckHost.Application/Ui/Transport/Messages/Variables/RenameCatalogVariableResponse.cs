namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class RenameCatalogVariableResponse
{
	public Variable? Variable { get; set; }

	public TransportError? Error { get; set; }
}
