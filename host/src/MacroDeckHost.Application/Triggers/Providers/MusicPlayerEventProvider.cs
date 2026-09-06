using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Triggers.Providers;

public sealed class MusicPlayerEventProvider : IHostEventProvider
{
	public const string ProviderIdValue = "music-player";

	public const string TrackChangedEventId = "track-changed";
	public const string PlaybackStartedEventId = "playback-started";
	public const string PlaybackPausedEventId = "playback-paused";
	public const string PlaybackStoppedEventId = "playback-stopped";
	public const string DeviceChangedEventId = "device-changed";
	public const string VolumeChangedEventId = "volume-changed";
	public const string PlayerConnectedEventId = "player-connected";
	public const string PlayerDisconnectedEventId = "player-disconnected";

	private readonly IEventBus _bus;
	private readonly Dictionary<string, MusicPlayerStatePayload> _previous = new(StringComparer.Ordinal);
	private readonly Lock _sync = new();

	public MusicPlayerEventProvider(IEventBus bus)
	{
		_bus = bus;
	}

	public string ProviderId => ProviderIdValue;

	public LocalizedText ProviderName => AppStrings.Events.MusicPlayer.ProviderName();

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = TrackChangedEventId,
			Name = AppStrings.Events.MusicPlayer.TrackChangedName(),
			Description = AppStrings.Events.MusicPlayer.TrackChangedDescription(),
			Category = AppStrings.Events.MusicPlayer.PlaybackCategory(),
			ConfigurationParameters = [InstanceFilter()],
			PayloadParameters =
			[
				Instance(),
				ActionParameter.Text("trackName", label: AppStrings.Events.MusicPlayer.TrackLabel()),
				ActionParameter.Text("artistName", label: AppStrings.Events.MusicPlayer.ArtistLabel()),
				ActionParameter.Text("albumName", label: AppStrings.Events.MusicPlayer.AlbumLabel()),
				ActionParameter.Text("previousTrackName", label: AppStrings.Events.MusicPlayer.PreviousTrackLabel())
			]
		},
		Playback(PlaybackStartedEventId, AppStrings.Events.MusicPlayer.PlaybackStartedName()),
		Playback(PlaybackPausedEventId, AppStrings.Events.MusicPlayer.PlaybackPausedName()),
		Playback(PlaybackStoppedEventId, AppStrings.Events.MusicPlayer.PlaybackStoppedName()),
		new()
		{
			Id = DeviceChangedEventId,
			Name = AppStrings.Events.MusicPlayer.DeviceChangedName(),
			Category = AppStrings.Events.MusicPlayer.PlaybackCategory(),
			ConfigurationParameters = [InstanceFilter()],
			PayloadParameters =
			[
				Instance(),
				ActionParameter.Text("deviceName", label: AppStrings.Events.Common.DeviceLabel()),
				ActionParameter.Text("previousDeviceName", label: AppStrings.Events.MusicPlayer.PreviousDeviceLabel())
			]
		},
		new()
		{
			Id = VolumeChangedEventId,
			Name = AppStrings.Events.MusicPlayer.VolumeChangedName(),
			Category = AppStrings.Events.MusicPlayer.PlaybackCategory(),
			ConfigurationParameters = [InstanceFilter()],
			PayloadParameters =
			[
				Instance(),
				ActionParameter.Number("volume", label: AppStrings.Events.MusicPlayer.VolumeLabel()),
				ActionParameter.Number("previousVolume", label: AppStrings.Events.MusicPlayer.PreviousVolumeLabel())
			]
		},
		Connection(PlayerConnectedEventId, AppStrings.Events.MusicPlayer.PlayerConnectedName()),
		Connection(PlayerDisconnectedEventId, AppStrings.Events.MusicPlayer.PlayerDisconnectedName())
	];

	public void Observe(MusicPlayerStatePayload current)
	{
		var instanceId = current.InstanceId;
		if (string.IsNullOrEmpty(instanceId))
		{
			return;
		}

		MusicPlayerStatePayload? previous;
		lock (_sync)
		{
			_previous.TryGetValue(instanceId, out previous);
			_previous[instanceId] = current;
		}

		if (previous is null)
		{
			return;
		}

		if (previous.IsConnected != current.IsConnected)
		{
			Publish(current.IsConnected ? PlayerConnectedEventId : PlayerDisconnectedEventId, instanceId);

			return;
		}

		if (!current.IsConnected)
		{
			return;
		}

		if (previous.PlaybackState != current.PlaybackState)
		{
			var eventId = current.PlaybackState switch
			{
				"playing" => PlaybackStartedEventId,
				"paused" => PlaybackPausedEventId,
				_ => PlaybackStoppedEventId
			};

			Publish(eventId, instanceId);
		}

		if (previous.TrackName != current.TrackName && !string.IsNullOrEmpty(current.TrackName))
		{
			Publish(TrackChangedEventId,
				instanceId,
				("trackName", current.TrackName),
				("artistName", current.ArtistName ?? string.Empty),
				("albumName", current.AlbumName ?? string.Empty),
				("previousTrackName", previous.TrackName ?? string.Empty));
		}

		if (previous.DeviceName != current.DeviceName && !string.IsNullOrEmpty(current.DeviceName))
		{
			Publish(DeviceChangedEventId,
				instanceId,
				("deviceName", current.DeviceName),
				("previousDeviceName", previous.DeviceName ?? string.Empty));
		}

		if (previous.Volume != current.Volume && current.Volume is { } volume)
		{
			Publish(VolumeChangedEventId,
				instanceId,
				("volume", (double)volume),
				("previousVolume", previous.Volume is { } p ? (double)p : 0d));
		}
	}

	public void Forget(IReadOnlyCollection<string> liveInstanceIds)
	{
		lock (_sync)
		{
			foreach (var stale in _previous.Keys.Where(id => !liveInstanceIds.Contains(id)).ToList())
			{
				_previous.Remove(stale);
			}
		}
	}

	private void Publish(string eventId, string instanceId, params (string Name, object? Value)[] parameters)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["instance"] = instanceId };
		foreach (var (name, value) in parameters)
		{
			values[name] = value;
		}

		_bus.Publish(new EventOccurrence(QualifiedEventId(eventId), values));
	}

	private static string QualifiedEventId(string eventId)
		=> QualifiedId.Create(ProviderIdValue, eventId, OwnerIdKind.HostProvider, LocalIdKind.Declared, "Event")
			.ToString();

	private static EventDefinition Playback(string id, LocalizedText name) => new()
	{
		Id = id,
		Name = name,
		Category = AppStrings.Events.MusicPlayer.PlaybackCategory(),
		ConfigurationParameters = [InstanceFilter()],
		PayloadParameters = [Instance()]
	};

	private static EventDefinition Connection(string id, LocalizedText name) => new()
	{
		Id = id,
		Name = name,
		Category = AppStrings.Events.MusicPlayer.ConnectionCategory(),
		ConfigurationParameters = [InstanceFilter()],
		PayloadParameters = [Instance()]
	};

	private static ActionParameter Instance() =>
		ActionParameter.DynamicChoice("instance",
			label: AppStrings.Events.MusicPlayer.PlayerLabel(),
			optionsSourceId: MusicPlayerOptionsSourceIds.Instances);

	private static ActionParameter InstanceFilter()
		=> ActionParameter.DynamicChoice("instance",
			label: AppStrings.Events.MusicPlayer.PlayerLabel(),
			description: AppStrings.Events.MusicPlayer.PlayerFilterDescription(),
			optionsSourceId: MusicPlayerOptionsSourceIds.Instances,
			placeholder: AppStrings.Events.MusicPlayer.AnyPlayerPlaceholder());
}
