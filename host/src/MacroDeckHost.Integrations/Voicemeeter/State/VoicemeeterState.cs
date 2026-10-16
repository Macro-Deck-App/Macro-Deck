using System.Globalization;
using MacroDeckHost.Integrations.Voicemeeter.Models;

namespace MacroDeckHost.Integrations.Voicemeeter.State;

internal enum VoicemeeterChannelKind
{
	Strip,
	Bus
}

internal sealed record VoicemeeterChannel(
	int Index,
	VoicemeeterChannelKind Kind,
	bool IsPhysical,
	string Label,
	float Gain,
	bool Muted,
	bool Mono,
	bool Solo,
	IReadOnlyDictionary<string, bool> Assignments)
{
	public string DefaultName(VoicemeeterLayout layout)
	{
		return Kind == VoicemeeterChannelKind.Strip
			? $"Strip {Index}"
			: $"Bus {layout.BusAssignmentName(Index) ?? (Index + 1).ToString(CultureInfo.InvariantCulture)}";
	}

	public string DisplayName(VoicemeeterLayout layout)
	{
		return string.IsNullOrWhiteSpace(Label) ? DefaultName(layout) : Label;
	}
}

internal sealed record VoicemeeterState
{
	public static readonly VoicemeeterState Disconnected = new();

	public bool IsConnected { get; init; }

	public VoicemeeterEdition Edition { get; init; } = VoicemeeterEdition.None;

	public string? Version { get; init; }

	public IReadOnlyList<VoicemeeterChannel> Strips { get; init; } = [];

	public IReadOnlyList<VoicemeeterChannel> Buses { get; init; } = [];

	public IReadOnlyDictionary<int, bool> MacroButtons { get; init; } =
		new Dictionary<int, bool>();

	public VoicemeeterLayout Layout => VoicemeeterLayout.For(Edition);

	public VoicemeeterChannel? Strip(int index)
	{
		return index >= 0 && index < Strips.Count ? Strips[index] : null;
	}

	public VoicemeeterChannel? Bus(int index)
	{
		return index >= 0 && index < Buses.Count ? Buses[index] : null;
	}

	public VoicemeeterChannel? Channel(VoicemeeterChannelKind kind, int index)
	{
		return kind == VoicemeeterChannelKind.Strip ? Strip(index) : Bus(index);
	}
}
