namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class DeleteProfileResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
