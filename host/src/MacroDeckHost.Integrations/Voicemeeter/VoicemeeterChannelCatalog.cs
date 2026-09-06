using System.Globalization;

namespace MacroDeckHost.Integrations.Voicemeeter;

internal sealed record VoicemeeterChannelOption(int Index, string Label)
{
	public string Value => Index.ToString(CultureInfo.InvariantCulture);
}

internal sealed record VoicemeeterChannelCatalog
{
	public static readonly VoicemeeterChannelCatalog Unknown = FromLayout(VoicemeeterLayout.Widest);

	public VoicemeeterEdition Edition { get; init; } = VoicemeeterEdition.None;

	public IReadOnlyList<VoicemeeterChannelOption> Strips { get; init; } = [];

	public IReadOnlyList<VoicemeeterChannelOption> Buses { get; init; } = [];

	public IReadOnlyList<string> BusAssignments { get; init; } = [];

	public IReadOnlyList<VoicemeeterChannelOption> For(VoicemeeterChannelKind kind)
		=> kind == VoicemeeterChannelKind.Strip ? Strips : Buses;

	public static VoicemeeterChannelCatalog FromState(VoicemeeterState state)
	{
		var layout = state.Layout;

		return new VoicemeeterChannelCatalog
		{
			Edition = state.Edition,
			Strips = state.Strips
				.Select(strip => new VoicemeeterChannelOption(strip.Index, strip.DisplayName(layout)))
				.ToList(),
			Buses = state.Buses
				.Select(bus => new VoicemeeterChannelOption(bus.Index, bus.DisplayName(layout)))
				.ToList(),
			BusAssignments = layout.BusAssignmentNames()
		};
	}

	private static VoicemeeterChannelCatalog FromLayout(VoicemeeterLayout layout) => new()
	{
		Strips = Enumerable.Range(0, layout.Strips)
			.Select(index => new VoicemeeterChannelOption(index, $"Strip {index + 1}"))
			.ToList(),
		Buses = Enumerable.Range(0, layout.Buses)
			.Select(index => new VoicemeeterChannelOption(index, $"Bus {layout.BusAssignmentName(index)}"))
			.ToList(),
		BusAssignments = layout.BusAssignmentNames()
	};
}
