using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Widgets;

/// <summary>
/// The inset a widget keeps between its content and its own edge.
///
/// <para>
/// A tile is a rounded rectangle, and a rounded corner eats into the box from the outside: content laid
/// out to the edge runs under the curve, which is a label whose first letter is sliced by the corner it
/// sits in. A corner of radius <c>r</c> intrudes <c>r(1 - 1/sqrt(2))</c> on its diagonal, and that is
/// the clearance the corner itself demands - a geometric consequence of the radius the reader draws.
/// </para>
///
/// <para>
/// Clearing the corner is not the same as being readable, though, and issue #874 is the difference: at
/// the default radius the curve only demands six reference pixels, which leaves content sitting all but
/// against the edge, and a tile drawn with a square corner demands none at all. So the clearance is
/// floored at <see cref="BreathingRoom" /> - the inset a tile keeps whatever its corner - and the
/// larger of the two wins. A radius sharp enough to intrude further still gets what it needs.
/// </para>
///
/// <para>
/// Expressed against the reference cell rather than the basis, because the radius does not grow with the
/// widget: a two-by-two tile is drawn with the same corner as a one-by-one, so the inset that clears it
/// is the same length, not the same fraction. <c>maxOfCell</c> is what says that in the profile, and the
/// basis term is what keeps the inset from swallowing a widget smaller than a cell.
/// </para>
///
/// <para>See ADR 0064 for why the radius reaches a view at all.</para>
/// </summary>
public static class WidgetSafeArea
{
	/// <summary>The share of a corner's radius that its curve intrudes on the diagonal: 1 - 1/sqrt(2).</summary>
	private static readonly double _cornerClearance = 1 - (1 / Math.Sqrt(2));

	/// <summary>What a surface that says nothing about its corners is drawn with.</summary>
	public const int DefaultCornerRadius = GridDefaults.WidgetBorderRadius;

	/// <summary>
	/// The inset a tile keeps regardless of its corner, in reference pixels. The gap between two tiles,
	/// so the space a reader sees inside a widget is the space it sees between widgets and the deck is
	/// one rhythm rather than two.
	/// </summary>
	public const int BreathingRoom = GridDefaults.WidgetSpacing;

	/// <summary>The inset for a tile drawn with <paramref name="cornerRadius" /> reference pixels.</summary>
	public static UiLength LengthFor(int cornerRadius)
	{
		var inset = Math.Max(BreathingRoom, _cornerClearance * Math.Max(0, cornerRadius));

		return UiLength.Capped(inset / UiLength.Cell, inset);
	}

	/// <summary><see cref="LengthFor" /> as a property value.</summary>
	public static UiSize For(int cornerRadius) => UiSize.Of(LengthFor(cornerRadius));

	/// <summary>
	/// The radius the tile this surface will be drawn in carries. A surface that states none - a preview,
	/// which has no tile, or a reader that predates the key - answers with the profile's own default
	/// rather than leaving its content in the corner.
	/// </summary>
	public static int RadiusOf(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.CornerRadius, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var radius)
				? radius
				: DefaultCornerRadius;
	}
}
