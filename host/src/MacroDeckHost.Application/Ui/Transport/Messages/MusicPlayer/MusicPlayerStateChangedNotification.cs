namespace MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

public class MusicPlayerStateChangedNotification
{
	public MusicPlayerStatePayload State { get; set; } = new();
}
