using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal static class ScreenSaverDrift
{
	public const int PositionCount = 9;

	private static readonly string[] _justify =
		[UiComponentJustify.Start, UiComponentJustify.Center, UiComponentJustify.End];

	private static readonly string[] _align =
		[UiComponentAlignments.Start, UiComponentAlignments.Center, UiComponentAlignments.End];

	public static UiValue<string> Justify(UiState<int> position)
		=> UiValue.From(() => _justify[Slot(position.Value) / 3]);

	public static UiValue<string> Align(UiState<int> position)
		=> UiValue.From(() => _align[Slot(position.Value) % 3]);

	private static int Slot(int position) => ((position % PositionCount) + PositionCount) % PositionCount;
}
