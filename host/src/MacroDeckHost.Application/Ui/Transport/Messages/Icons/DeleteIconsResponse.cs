namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class DeleteIconsResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public int DeletedCount { get; set; }
}
