using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Widgets;

/// <summary>
/// The inset a widget keeps between its content and the edge of its rounded tile. A rounded corner of
/// radius <c>r</c> intrudes <c>r(1 - 1/sqrt(2))</c> on its diagonal; the inset is that clearance, floored at
/// <see cref="BreathingRoom" /> so content never sits against the edge of a tile with a small or square
/// corner. The inset is a length in the reference coordinate space capped at a fraction of
/// <see cref="UiLength.Cell" />, because the corner does not grow with the widget.
/// </summary>
public static class UiWidgetSafeArea
{
	private static readonly double _cornerClearance = 1 - 1 / Math.Sqrt(2);

	/// <summary>The corner radius Macro Deck draws a tile with when its surface states none, in reference
	/// pixels.</summary>
	public const int DefaultCornerRadius = 22;

	/// <summary>The inset a tile keeps regardless of its corner, in reference pixels: the default gap between
	/// two tiles.</summary>
	public const int BreathingRoom = 12;

	/// <summary>The inset for a tile drawn with <paramref name="cornerRadius" /> reference pixels. A negative
	/// radius reads as a square corner.</summary>
	public static UiLength LengthFor(int cornerRadius)
	{
		var inset = Math.Max(BreathingRoom, _cornerClearance * Math.Max(0, cornerRadius));

		return UiLength.Capped(inset / UiLength.Cell, inset);
	}

	/// <summary><see cref="LengthFor" /> as a property value, for a padding or a gap.</summary>
	public static UiSize For(int cornerRadius)
	{
		return UiSize.Of(LengthFor(cornerRadius));
	}

	/// <summary>
	/// The corner radius the tile this surface is drawn in carries, from
	/// <see cref="UiWidgetSurfaceAttributes.CornerRadius" />. A surface that states none, such as a preview,
	/// answers <see cref="DefaultCornerRadius" />.
	/// </summary>
	public static int RadiusOf(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.CornerRadius, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var radius)
				? radius
				: DefaultCornerRadius;
	}

	/// <summary>The inset for the tile this surface is drawn in: <see cref="For" /> of
	/// <see cref="RadiusOf" />.</summary>
	public static UiSize For(UiSurface surface)
	{
		return For(RadiusOf(surface));
	}
}
