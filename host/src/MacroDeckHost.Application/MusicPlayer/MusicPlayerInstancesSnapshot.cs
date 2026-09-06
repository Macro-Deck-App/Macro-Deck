using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public interface IMusicPlayerInstancesSnapshot
{
	IReadOnlyList<MusicPlayerInstanceDto> Instances { get; }

	void Record(IReadOnlyList<MusicPlayerInstanceDto> instances);
}

public sealed class MusicPlayerInstancesSnapshot : IMusicPlayerInstancesSnapshot
{
	private volatile IReadOnlyList<MusicPlayerInstanceDto> _instances = [];

	public IReadOnlyList<MusicPlayerInstanceDto> Instances => _instances;

	public void Record(IReadOnlyList<MusicPlayerInstanceDto> instances) => _instances = instances;
}
