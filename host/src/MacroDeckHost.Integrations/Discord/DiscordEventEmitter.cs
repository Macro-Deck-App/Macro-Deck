using System.Text.Json;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Discord;

internal sealed class DiscordEventEmitter
{
	private readonly IEventPublisher _publisher;
	private DiscordState? _previous;

	public DiscordEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Observe(DiscordState current)
	{
		var previous = _previous;
		_previous = current;

		if (previous is null)
		{
			if (current.IsConnected)
			{
				_publisher.Publish(DiscordEventIds.Connected);
			}

			return;
		}

		if (previous.IsConnected != current.IsConnected)
		{
			_publisher.Publish(current.IsConnected ? DiscordEventIds.Connected : DiscordEventIds.Disconnected);

			return;
		}

		if (!current.IsConnected)
		{
			return;
		}

		PublishToggle(previous.SelfMuted, current.SelfMuted, DiscordEventIds.Muted, DiscordEventIds.Unmuted);
		PublishToggle(previous.SelfDeafened,
			current.SelfDeafened,
			DiscordEventIds.Deafened,
			DiscordEventIds.Undeafened);
		PublishToggle(previous.ServerMuted,
			current.ServerMuted,
			DiscordEventIds.ServerMuted,
			DiscordEventIds.ServerUnmuted);
		PublishToggle(previous.ServerDeafened,
			current.ServerDeafened,
			DiscordEventIds.ServerDeafened,
			DiscordEventIds.ServerUndeafened);

		PublishToggle(previous.EffectivelyMuted,
			current.EffectivelyMuted,
			DiscordEventIds.MicrophoneSilenced,
			DiscordEventIds.MicrophoneLive);

		PublishVoiceChannelChange(previous, current);

		if (previous.VoiceConnectionState != current.VoiceConnectionState &&
			current.VoiceConnectionState is not null)
		{
			_publisher.Publish(DiscordEventIds.VoiceConnectionStateChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["state"] = current.VoiceConnectionState,
					["averagePing"] = current.AveragePing ?? 0
				});
		}
	}

	public void Reset() => _previous = null;

	public void PublishSpeaking(string userId, bool isSelf, bool speaking)
		=> _publisher.Publish(speaking ? DiscordEventIds.SpeakingStarted : DiscordEventIds.SpeakingStopped,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["userId"] = userId,
				["isSelf"] = isSelf
			});

	public void PublishNotification(JsonElement data)
		=> _publisher.Publish(DiscordEventIds.NotificationReceived,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["title"] = DiscordStateMapper.ReadString(data, "title") ?? string.Empty,
				["body"] = DiscordStateMapper.ReadString(data, "body") ?? string.Empty,
				["channelId"] = DiscordStateMapper.ReadString(data, "channel_id") ?? string.Empty,
				["iconUrl"] = DiscordStateMapper.ReadString(data, "icon_url") ?? string.Empty
			});

	private void PublishVoiceChannelChange(DiscordState previous, DiscordState current)
	{
		if (string.Equals(previous.VoiceChannelId, current.VoiceChannelId, StringComparison.Ordinal))
		{
			return;
		}

		if (previous.VoiceChannelId is not null)
		{
			_publisher.Publish(DiscordEventIds.VoiceChannelLeft,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["channelName"] = previous.VoiceChannelName ?? string.Empty,
					["channelId"] = previous.VoiceChannelId
				});
		}

		if (current.VoiceChannelId is not null)
		{
			_publisher.Publish(DiscordEventIds.VoiceChannelJoined,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["channelName"] = current.VoiceChannelName ?? string.Empty,
					["channelId"] = current.VoiceChannelId,
					["guildName"] = current.VoiceGuildName ?? string.Empty,
					["guildId"] = current.VoiceGuildId ?? string.Empty
				});
		}
	}

	private void PublishToggle(bool previous, bool current, string onEventId, string offEventId)
	{
		if (previous != current)
		{
			_publisher.Publish(current ? onEventId : offEventId);
		}
	}
}
