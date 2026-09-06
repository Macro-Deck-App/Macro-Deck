namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class ResolveCatalogVariableResponse
{
	public VariableCatalogNodeDto? Node { get; set; }

	public TransportError? Error { get; set; }
}
