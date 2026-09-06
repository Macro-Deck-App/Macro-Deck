namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class ProfileCreatedEvent
{
	public Profile Profile { get; set; } = new();
}
