namespace MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

public class MusicPlayerInstancesChangedNotification
{
	public List<MusicPlayerInstanceDto> Instances { get; set; } = new();
}
