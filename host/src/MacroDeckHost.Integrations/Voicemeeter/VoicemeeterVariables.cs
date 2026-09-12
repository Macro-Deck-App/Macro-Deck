using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter;

internal static class VoicemeeterVariables
{
	public const string Connected = "voicemeeter_connected";
	public const string Edition = "voicemeeter_edition";
	public const string Version = "voicemeeter_version";

	// Voicemeeter's own fader range, in decibels, and the granularity its UI moves in.
	public const double MinimumGain = -60d;
	public const double MaximumGain = 12d;
	public const double GainStep = 0.1d;

	private const string DecibelUnit = "dB";

	private const string StripPrefix = "voicemeeter_strip";
	private const string BusPrefix = "voicemeeter_bus";

	private const string NameSuffix = "_name";
	private const string GainSuffix = "_gain";
	private const string MutedSuffix = "_muted";

	private static readonly TimeSpan _liveInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _slowInterval = TimeSpan.FromSeconds(15);

	public static IReadOnlyList<VariableDefinition> All { get; } = Build();

	public static VariableReading Read(VoicemeeterState state, string name)
	{
		switch (name)
		{
			case Connected:
				// The one variable that answers while Voicemeeter is closed. Everything else reads as
				// unknown, because a closed Voicemeeter is not a silent one.
				return VariableReading.Of(state.IsConnected);
			case Edition:
				return VariableReading.Of(state.IsConnected ? VoicemeeterEditions.DisplayName(state.Edition) : null);
			case Version:
				return VariableReading.Of(state.IsConnected ? state.Version : null);
		}

		if (!state.IsConnected || !TryParse(name, out var kind, out var index, out var suffix))
		{
			return VariableReading.Unavailable;
		}

		var channel = state.Channel(kind, index);
		if (channel is null)
		{
			return VariableReading.Unavailable;
		}

		return suffix switch
		{
			NameSuffix => VariableReading.Of(channel.DisplayName(state.Layout)),
			GainSuffix => VariableReading.Of(Math.Round(channel.Gain, 1), MinimumGain, MaximumGain, GainStep),
			MutedSuffix => VariableReading.Of(channel.Muted),
			_ => ReadRouting(channel, suffix)
		};
	}

	private static VariableReading ReadRouting(VoicemeeterChannel channel, string suffix)
		=> channel.Kind == VoicemeeterChannelKind.Strip &&
			channel.Assignments.TryGetValue(suffix[1..].ToUpperInvariant(), out var routed)
				? VariableReading.Of(routed)
				: VariableReading.Unavailable;

	public static VariableWriteResult Write(VoicemeeterConnection connection, string name, double decibels)
	{
		if (!TryParse(name, out var kind, out var index, out var suffix) || suffix != GainSuffix)
		{
			return VariableWriteResult.NotWritable();
		}

		// The declared set covers the widest edition, so a strip the running edition does not have is a
		// legitimate variable with nothing behind it right now - the same answer its read gives.
		if (connection.State.Channel(kind, index) is null)
		{
			return VariableWriteResult.Unavailable();
		}

		var parameter = kind == VoicemeeterChannelKind.Strip
			? VoicemeeterParameters.Strip(index, VoicemeeterParameters.Gain)
			: VoicemeeterParameters.Bus(index, VoicemeeterParameters.Gain);

		connection.SetParameter(parameter, (float)Math.Clamp(decibels, MinimumGain, MaximumGain));

		return VariableWriteResult.Applied();
	}

	private static List<VariableDefinition> Build()
	{
		var layout = VoicemeeterLayout.Widest;
		var variables = new List<VariableDefinition>
		{
			VariableDefinition.Eager(Connected, VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = MacroDeckStrings.Connection.Connected()
				},
			VariableDefinition.Eager(Edition, VariableType.Text, refreshInterval: _slowInterval)
				with
				{
					DisplayName = AppStrings.Integrations.Voicemeeter.Variables.Edition()
				},
			VariableDefinition.Eager(Version, VariableType.Text, refreshInterval: _slowInterval)
				with
				{
					DisplayName = AppStrings.Integrations.Voicemeeter.Variables.Version()
				}
		};

		for (var index = 0; index < layout.Strips; index++)
		{
			AddChannel(variables, StripPrefix, index);
			AddRouting(variables, index, layout);
		}

		for (var index = 0; index < layout.Buses; index++)
		{
			AddChannel(variables, BusPrefix, index);
		}

		return variables;
	}

	private static void AddChannel(List<VariableDefinition> variables, string prefix, int index)
	{
		var channel = string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");

		variables.Add(VariableDefinition.Eager(channel + NameSuffix, VariableType.Text, refreshInterval: _slowInterval)
			with
			{
				DisplayName = AppStrings.Integrations.Voicemeeter.Variables.ChannelName()
			});
		variables.Add(VariableDefinition.Eager(channel + GainSuffix, VariableType.Numeric, 1, _liveInterval)
			with
			{
				DisplayName = AppStrings.Integrations.Voicemeeter.Variables.ChannelGain(),
				Unit = DecibelUnit,
				SemanticKind = VariableSemanticKinds.None,
				Write = new VariableWriteCapability()
			});
		variables.Add(
			VariableDefinition.Eager(channel + MutedSuffix, VariableType.Boolean, refreshInterval: _liveInterval)
				with
				{
					DisplayName = AppStrings.Integrations.Voicemeeter.Variables.ChannelMuted()
				});
	}

	private static void AddRouting(List<VariableDefinition> variables, int strip, VoicemeeterLayout layout)
	{
		foreach (var bus in layout.BusAssignmentNames())
		{
			var name = string.Create(CultureInfo.InvariantCulture,
				$"{StripPrefix}{strip}_{bus.ToLowerInvariant()}");

			variables.Add(VariableDefinition.Eager(name, VariableType.Boolean, refreshInterval: _liveInterval)
				with
				{
					DisplayName = AppStrings.Integrations.Voicemeeter.Variables.StripRouted(bus: bus)
				});
		}
	}

	private static bool TryParse(string name, out VoicemeeterChannelKind kind, out int index, out string suffix)
	{
		kind = VoicemeeterChannelKind.Strip;
		index = -1;
		suffix = string.Empty;

		string remainder;
		if (name.StartsWith(StripPrefix, StringComparison.Ordinal))
		{
			remainder = name[StripPrefix.Length..];
		}
		else if (name.StartsWith(BusPrefix, StringComparison.Ordinal))
		{
			kind = VoicemeeterChannelKind.Bus;
			remainder = name[BusPrefix.Length..];
		}
		else
		{
			return false;
		}

		var separator = remainder.IndexOf('_', StringComparison.Ordinal);
		if (separator <= 0)
		{
			return false;
		}

		suffix = remainder[separator..];
		return int.TryParse(remainder[..separator],
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out index);
	}
}
