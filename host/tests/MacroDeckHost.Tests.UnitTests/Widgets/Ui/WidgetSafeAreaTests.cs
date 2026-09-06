using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// A tile is a rounded rectangle, and a rounded corner eats into the box from the outside: content laid
/// out to the edge runs under the curve. Clearing the curve is not the same as being readable, though,
/// so the inset is the larger of what the corner demands and the breathing room every tile keeps
/// (issue #874). These check both halves rather than a number somebody liked. See ADR 0064.
/// </summary>
[TestFixture]
public class WidgetSafeAreaTests
{
	/// <summary>
	/// What issue #874 asked for, stated from the requirement rather than from the constant the fix
	/// introduced: the space a reader sees inside a tile is the space it sees between two tiles.
	/// </summary>
	private const double _breathingRoom = GridDefaults.WidgetSpacing;

	private static UiSurface Surface(int? cornerRadius)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (cornerRadius is { } radius)
		{
			attributes[UiWidgetSurfaceAttributes.CornerRadius] = JsonSerializer.SerializeToElement(radius);
		}

		return new UiSurface
		{
			Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared, Attributes = attributes
		};
	}

	private static double Clearance(int cornerRadius)
		=> WidgetSafeArea.LengthFor(cornerRadius).MaxOfCell!.Value * UiLength.Cell;

	/// <summary>
	/// What issue #874 reported: at the radius a deck is drawn with by default the curve demands only
	/// about six reference pixels, which is not enough space for a reader, so the tile keeps the
	/// breathing room instead.
	/// </summary>
	[Test]
	public void KeepsTheBreathingRoomWhenTheCornerDemandsLess()
	{
		Assert.That(Clearance(WidgetSafeArea.DefaultCornerRadius),
			Is.EqualTo(_breathingRoom).Within(0.001));
	}

	/// <summary>The floor holds all the way down to a tile with no corner at all to clear.</summary>
	[Test]
	public void NeverInsetsLessThanTheBreathingRoom()
	{
		Assert.Multiple(() =>
		{
			foreach (var radius in new[] { 0, 1, 8, 22, 40 })
			{
				Assert.That(Clearance(radius),
					Is.GreaterThanOrEqualTo(_breathingRoom - 0.001),
					$"radius {radius}");
			}
		});
	}

	/// <summary>
	/// The corner guarantee is unchanged: a radius that intrudes further than the breathing room still
	/// gets exactly what its diagonal eats - r(1 - 1/sqrt(2)) - rather than sticking at the floor.
	/// </summary>
	[Test]
	public void ClearsWhatTheCornerIntrudesOnItsDiagonalWhenThatIsMore()
	{
		Assert.Multiple(() =>
		{
			// r(1 - 1/sqrt(2)) at r = 60 and r = 120.
			Assert.That(Clearance(60), Is.EqualTo(17.5736).Within(0.001));
			Assert.That(Clearance(120), Is.EqualTo(35.1472).Within(0.001));
		});
	}

	/// <summary>
	/// The radius does not grow with the widget - a two-by-two tile carries the same corner as a
	/// one-by-one - so the inset that clears it is the same length, not the same fraction. Expressed
	/// against the basis alone it would grow with the widget and leave a large tile over-padded.
	/// </summary>
	[Test]
	public void IsTheSameLengthWhateverTheWidgetSpans()
	{
		var length = WidgetSafeArea.LengthFor(22);
		var cap = length.MaxOfCell!.Value * UiLength.Cell;

		var oneCell = Math.Min(length.Basis * 120d, cap);
		var fourCells = Math.Min(length.Basis * 252d, cap);

		Assert.That(fourCells, Is.EqualTo(oneCell).Within(0.001));
	}

	[Test]
	public void FollowsTheRadiusTheSurfaceStates()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Clearance(WidgetSafeArea.RadiusOf(Surface(44))), Is.EqualTo(12.8874).Within(0.001));
			// A square tile has no corner to clear, and still keeps its content off the edge.
			Assert.That(Clearance(WidgetSafeArea.RadiusOf(Surface(0))),
				Is.EqualTo(_breathingRoom).Within(0.001));
		});
	}

	/// <summary>
	/// The compatibility case the key is additive for: a surface built by a reader that predates it - and
	/// every preview, which has no tile at all - still keeps its content out of the corner rather than
	/// rendering with none.
	/// </summary>
	[Test]
	public void FallsBackToTheProfileDefaultWhenTheSurfaceSaysNothing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WidgetSafeArea.RadiusOf(Surface(null)), Is.EqualTo(WidgetSafeArea.DefaultCornerRadius));
			Assert.That(Clearance(WidgetSafeArea.RadiusOf(Surface(null))), Is.EqualTo(Clearance(22)).Within(0.001));
		});
	}

	/// <summary>A malformed value is not a reason to draw into the corner either.</summary>
	[Test]
	public void FallsBackWhenTheStatedRadiusIsNotANumber()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.CornerRadius] = JsonSerializer.SerializeToElement("22")
			}
		};

		Assert.That(WidgetSafeArea.RadiusOf(surface), Is.EqualTo(WidgetSafeArea.DefaultCornerRadius));
	}
}
