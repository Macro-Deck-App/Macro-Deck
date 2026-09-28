namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class DuplicateProfileResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Profile? Profile { get; set; }
}
