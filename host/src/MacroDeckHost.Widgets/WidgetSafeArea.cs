using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Widgets;

namespace MacroDeckHost.Widgets;

public static class WidgetSafeArea
{
	public const int DefaultCornerRadius = UiWidgetSafeArea.DefaultCornerRadius;

	public const int BreathingRoom = UiWidgetSafeArea.BreathingRoom;

	public static UiLength LengthFor(int cornerRadius)
	{
		return UiWidgetSafeArea.LengthFor(cornerRadius);
	}

	public static UiSize For(int cornerRadius)
	{
		return UiWidgetSafeArea.For(cornerRadius);
	}

	public static int RadiusOf(UiSurface surface)
	{
		return UiWidgetSafeArea.RadiusOf(surface);
	}
}
