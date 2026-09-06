namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class UpdateIconPackResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public IconPack? Pack { get; set; }
}
