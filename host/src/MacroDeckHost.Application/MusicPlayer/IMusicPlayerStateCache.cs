using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public interface IMusicPlayerStateCache
{
	MusicPlayerStatePayload? GetState(string instanceId);

	IReadOnlyList<MusicPlayerStatePayload> GetAll();

	void Record(string instanceId, MusicPlayerStatePayload payload);

	void Forget(IReadOnlySet<string> keep);
}
