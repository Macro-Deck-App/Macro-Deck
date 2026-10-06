namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class MoveProfileResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public List<ProfilePlacement>? Profiles { get; set; }
}
