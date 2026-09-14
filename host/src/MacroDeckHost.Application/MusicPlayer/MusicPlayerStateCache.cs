using System.Collections.Concurrent;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class MusicPlayerStateCache : IMusicPlayerStateCache
{
	private readonly ConcurrentDictionary<string, MusicPlayerStatePayload> _states =
		new(StringComparer.Ordinal);

	// Written only by the broadcast tick, which records instances one after another; readers are
	// widget sessions on other threads, for which an atomic reference write is enough.
	private string? _activeInstanceId;

	public MusicPlayerStatePayload? GetState(string instanceId)
		=> _states.GetValueOrDefault(instanceId);

	public IReadOnlyList<MusicPlayerStatePayload> GetAll() => _states.Values.ToList();

	public string? ActiveInstanceId => _activeInstanceId;

	public void Record(string instanceId, MusicPlayerStatePayload payload)
	{
		_states.TryGetValue(instanceId, out var previous);
		_states[instanceId] = payload;

		if (ClaimsFocus(previous, payload))
		{
			_activeInstanceId = instanceId;
		}
		else if (!payload.IsConnected && string.Equals(_activeInstanceId, instanceId, StringComparison.Ordinal))
		{
			_activeInstanceId = AnotherPlaying(instanceId);
		}
	}

	public void Forget(IReadOnlySet<string> keep)
	{
		foreach (var instanceId in _states.Keys.Where(id => !keep.Contains(id)))
		{
			_states.TryRemove(instanceId, out _);
		}

		if (_activeInstanceId is { } active && !keep.Contains(active))
		{
			_activeInstanceId = AnotherPlaying(active);
		}
	}

	private string? AnotherPlaying(string released)
		=> _states.FirstOrDefault(pair => pair.Value is { IsConnected: true, IsPlaying: true } &&
			!string.Equals(pair.Key, released, StringComparison.Ordinal)).Key;

	private bool ClaimsFocus(MusicPlayerStatePayload? previous, MusicPlayerStatePayload current)
	{
		if (current is not { IsConnected: true, IsPlaying: true })
		{
			return false;
		}

		if (previous is null)
		{
			return _activeInstanceId is null ||
				_states.GetValueOrDefault(_activeInstanceId) is not { IsConnected: true, IsPlaying: true };
		}

		if (!previous.IsPlaying)
		{
			return true;
		}

		return !string.IsNullOrEmpty(current.TrackName) &&
			!string.Equals(previous.TrackName, current.TrackName, StringComparison.Ordinal);
	}
}
