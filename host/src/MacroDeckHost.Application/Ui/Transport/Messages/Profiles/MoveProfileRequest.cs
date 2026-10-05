namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class MoveProfileRequest
{
	public string Id { get; set; } = string.Empty;
	public string TargetId { get; set; } = string.Empty;
	public string Position { get; set; } = string.Empty;
}
