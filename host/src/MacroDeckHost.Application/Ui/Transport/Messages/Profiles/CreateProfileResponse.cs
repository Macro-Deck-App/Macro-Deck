namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class CreateProfileResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Profile? Profile { get; set; }
}
