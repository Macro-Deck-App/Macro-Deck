using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public interface IMusicPlayerClientSync
{
	IReadOnlyList<(string Name, object Payload)> BuildHandshake();
}

public sealed class MusicPlayerClientSync : IMusicPlayerClientSync
{
	private readonly IMusicPlayerInstancesSnapshot _instances;
	private readonly IMusicPlayerStateCache _states;

	public MusicPlayerClientSync(IMusicPlayerInstancesSnapshot instances, IMusicPlayerStateCache states)
	{
		_instances = instances;
		_states = states;
	}

	public IReadOnlyList<(string Name, object Payload)> BuildHandshake()
	{
		var messages = new List<(string, object)>
		{
			(nameof(MusicPlayerInstancesChangedNotification),
				new MusicPlayerInstancesChangedNotification { Instances = _instances.Instances.ToList() })
		};
		messages.AddRange(_states.GetAll()
			.Select(state => ((string)nameof(MusicPlayerStateChangedNotification),
				(object)new MusicPlayerStateChangedNotification { State = state })));
		return messages;
	}
}
