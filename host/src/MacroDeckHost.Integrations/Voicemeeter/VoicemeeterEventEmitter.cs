using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Voicemeeter;

internal sealed class VoicemeeterEventEmitter
{
	private const float GainEpsilon = 0.1f;

	private readonly IEventPublisher _publisher;

	private VoicemeeterState? _previous;

	public VoicemeeterEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Observe(VoicemeeterState current)
	{
		var previous = _previous;
		_previous = current;

		if (previous is null)
		{
			if (current.IsConnected)
			{
				PublishConnected(current);
			}

			return;
		}

		if (previous.IsConnected != current.IsConnected)
		{
			if (current.IsConnected)
			{
				PublishConnected(current);
			}
			else
			{
				_publisher.Publish(VoicemeeterEventIds.Disconnected);
			}

			return;
		}

		if (!current.IsConnected)
		{
			return;
		}

		DiffChannels(previous, current, VoicemeeterChannelKind.Strip);
		DiffChannels(previous, current, VoicemeeterChannelKind.Bus);
		DiffMacroButtons(previous, current);
	}

	private void DiffChannels(VoicemeeterState previous, VoicemeeterState current, VoicemeeterChannelKind kind)
	{
		var layout = current.Layout;
		var channelKey = VoicemeeterEventDefinitions.ChannelParameterName(kind);
		var isStrip = kind == VoicemeeterChannelKind.Strip;

		var channels = isStrip ? current.Strips : current.Buses;
		foreach (var channel in channels)
		{
			var before = previous.Channel(kind, channel.Index);
			if (before is null)
			{
				continue;
			}

			var name = channel.DisplayName(layout);

			if (before.Muted != channel.Muted)
			{
				_publisher.Publish(isStrip ? VoicemeeterEventIds.StripMuteChanged : VoicemeeterEventIds.BusMuteChanged,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						[channelKey] = channel.Index,
						["name"] = name,
						["muted"] = channel.Muted
					});
			}

			if (Math.Abs(before.Gain - channel.Gain) >= GainEpsilon)
			{
				_publisher.Publish(isStrip ? VoicemeeterEventIds.StripGainChanged : VoicemeeterEventIds.BusGainChanged,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						[channelKey] = channel.Index,
						["name"] = name,
						["gain"] = Math.Round(channel.Gain, 1),
						["previousGain"] = Math.Round(before.Gain, 1)
					});
			}

			if (isStrip)
			{
				DiffRouting(before, channel, name);
			}
		}
	}

	private void DiffRouting(VoicemeeterChannel before, VoicemeeterChannel current, string name)
	{
		foreach (var (bus, enabled) in current.Assignments)
		{
			if (before.Assignments.TryGetValue(bus, out var wasEnabled) && wasEnabled == enabled)
			{
				continue;
			}

			_publisher.Publish(VoicemeeterEventIds.StripRoutingChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["strip"] = current.Index,
					["name"] = name,
					["bus"] = bus,
					["enabled"] = enabled
				});
		}
	}

	private void DiffMacroButtons(VoicemeeterState previous, VoicemeeterState current)
	{
		foreach (var (button, state) in current.MacroButtons)
		{
			if (previous.MacroButtons.TryGetValue(button, out var before) && before == state)
			{
				continue;
			}

			_publisher.Publish(VoicemeeterEventIds.MacroButtonChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["button"] = button,
					["state"] = state
				});
		}
	}

	private void PublishConnected(VoicemeeterState state)
		=> _publisher.Publish(VoicemeeterEventIds.Connected,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["edition"] = VoicemeeterEditions.DisplayName(state.Edition),
				["version"] = state.Version ?? string.Empty
			});
}
