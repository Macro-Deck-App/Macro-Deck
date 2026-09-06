namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class ProfileUpdatedEvent
{
	public Profile Profile { get; set; } = new();
}
