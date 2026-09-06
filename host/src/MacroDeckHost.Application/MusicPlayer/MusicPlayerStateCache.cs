using System.Collections.Concurrent;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class MusicPlayerStateCache : IMusicPlayerStateCache
{
	private readonly ConcurrentDictionary<string, MusicPlayerStatePayload> _states =
		new(StringComparer.Ordinal);

	public MusicPlayerStatePayload? GetState(string instanceId)
		=> _states.GetValueOrDefault(instanceId);

	public IReadOnlyList<MusicPlayerStatePayload> GetAll() => _states.Values.ToList();

	public void Record(string instanceId, MusicPlayerStatePayload payload)
		=> _states[instanceId] = payload;

	public void Forget(IReadOnlySet<string> keep)
	{
		foreach (var instanceId in _states.Keys.Where(id => !keep.Contains(id)))
		{
			_states.TryRemove(instanceId, out _);
		}
	}
}
