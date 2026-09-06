using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class MusicPlayerStateChangedEventArgs : EventArgs
{
	public MusicPlayerStateChangedEventArgs(string instanceId, MusicPlayerStatePayload state)
	{
		InstanceId = instanceId;
		State = state;
	}

	public string InstanceId { get; }

	public MusicPlayerStatePayload State { get; }
}

/// <summary>
/// The in-process counterpart of the music player's client notifications. A UI session cannot subscribe
/// to what goes out over the transport, and giving it a poller of its own would read every provider a
/// second time - so the one background service that already polls raises this beside each send, exactly
/// as <see cref="Weather.IWeatherStateNotifier" /> does for the weather card.
/// </summary>
public interface IMusicPlayerStateNotifier
{
	event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;

	/// <summary>Raised when the set of configured instances changed - a widget bound to an instance that
	/// has just appeared or disappeared has to reconsider which one it is showing.</summary>
	event EventHandler? InstancesChanged;

	void Publish(string instanceId, MusicPlayerStatePayload state);

	void PublishInstancesChanged();
}

public sealed class MusicPlayerStateNotifier : IMusicPlayerStateNotifier
{
	public event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;

	public event EventHandler? InstancesChanged;

	public void Publish(string instanceId, MusicPlayerStatePayload state)
		=> StateChanged?.Invoke(this, new MusicPlayerStateChangedEventArgs(instanceId, state));

	public void PublishInstancesChanged() => InstancesChanged?.Invoke(this, EventArgs.Empty);
}
