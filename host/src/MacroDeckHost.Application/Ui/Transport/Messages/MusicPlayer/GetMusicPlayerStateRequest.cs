namespace MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

public class GetMusicPlayerStateRequest
{
	public string? InstanceId { get; set; }
}

public class GetMusicPlayerStateResponse
{
	public MusicPlayerStatePayload? State { get; set; }
}
