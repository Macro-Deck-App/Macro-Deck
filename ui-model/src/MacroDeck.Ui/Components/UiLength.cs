using System.Text.Json.Serialization;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Components;

/// <summary>
/// A length expressed as a fraction of the widget's <b>basis</b> - the smaller side of the widget's content
/// box - optionally clamped against a fraction of the containing stack's cross extent.
///
/// <para>
/// <b>Why not pixels.</b> A widget is drawn at whatever size its grid cell happens to be, on clients whose
/// pixel densities and cell sizes have nothing in common. A tree carrying pixels would have to be rebuilt on
/// every resize, which means a host round-trip for a purely visual change. Expressed as fractions, one tree
/// is correct at every size and a resize never leaves the client.
/// </para>
///
/// <para>
/// <b>What <see cref="MaxOfCross" /> is for.</b> Some lengths must additionally not outgrow the row or column
/// they sit in - a forecast row's icon and text shrink with the row once enough rows share the height. The
/// resolved length is <c>min(Basis * basis, MaxOfCross * crossExtent)</c>, where <c>crossExtent</c> is the
/// containing stack's extent on its cross axis. A reader that cannot determine the cross extent ignores the
/// clamp rather than guessing.
/// </para>
///
/// <para>
/// <b>What <see cref="MaxOfCell" /> is for.</b> Some lengths must stop growing altogether once the widget
/// passes a certain size - a card whose caption stays the same size whether it occupies one cell or nine,
/// so the reading order does not invert as the widget grows. That cap cannot be a fraction of the basis,
/// which is the very thing it has to stop tracking, so it is a fraction of the <see cref="Cell" /> - the
/// one extent in this profile that does not move with the widget. A reader that lays widgets out on
/// something other than a cell grid, and so cannot determine a cell extent, ignores the clamp rather than
/// guessing.
/// </para>
/// </summary>
public sealed record UiLength
{
	/// <summary>
	/// The extent of one deck cell in the widget profile's reference coordinate space, and the anchor
	/// <see cref="MaxOfCell" /> is a fraction of.
	///
	/// <para>
	/// A widget occupies a whole number of cells, so this is the one extent a widget view can rely on that
	/// its own size does not change. Its value is a definition rather than a measurement: a reader lays a
	/// widget's content out in this space and scales the result to whatever the real cell size is, so the
	/// number only has to be the same on both sides of the wire, never the same as any device's pixels.
	/// </para>
	/// </summary>
	public const double Cell = 120d;

	/// <summary>The fraction of the widget basis this length resolves to before any clamp.</summary>
	[JsonPropertyOrder(0)]
	public required double Basis { get; init; }

	/// <summary>The fraction of the containing stack's cross extent this length is additionally clamped to.
	/// Omitted when absent, which means no clamp.</summary>
	[JsonPropertyOrder(1)]
	public double? MaxOfCross { get; init; }

	/// <summary>The fraction of <see cref="Cell" /> this length is additionally clamped to. Omitted when
	/// absent, which means no clamp. Composes with <see cref="MaxOfCross" />: a length carrying both
	/// resolves to the smallest of the three.</summary>
	[JsonPropertyOrder(2)]
	public double? MaxOfCell { get; init; }

	/// <summary>A length that is purely a fraction of the widget basis.</summary>
	public static UiLength OfBasis(double basis) => new() { Basis = basis };

	/// <summary>A length clamped against the containing stack's cross extent.</summary>
	public static UiLength OfBasis(double basis, double maxOfCross)
		=> new() { Basis = basis, MaxOfCross = maxOfCross };

	/// <summary>A length that stops growing at <paramref name="referenceExtent" />, stated in the same
	/// reference space as <see cref="Cell" /> so a call site can write the extent it means rather than
	/// dividing by the cell itself.</summary>
	public static UiLength Capped(double basis, double referenceExtent)
		=> new() { Basis = basis, MaxOfCell = referenceExtent / Cell };

	/// <summary>Reads as <see cref="OfBasis(double)" />, so a bare fraction is assignable.</summary>
	public static implicit operator UiLength(double basis) => OfBasis(basis);
}

/// <summary>
/// A length-valued element property: a constant, a computed one, or absent. <c>default(UiSize)</c> is
/// the absent value.
/// </summary>
/// <remarks>
/// Its own type rather than <c>UiValue&lt;UiLength&gt;</c> for the same reason
/// <see cref="UiText" /> is: C# applies at most one user-defined conversion, so <c>Size = 0.17</c> could not
/// travel from <c>double</c> through <see cref="UiLength" /> to <c>UiValue&lt;UiLength&gt;</c>.
/// </remarks>
public readonly record struct UiSize
{
	private UiSize(UiValue<UiLength> value) => Value = value;

	/// <summary>The underlying value, evaluated by the runtime when it composes node properties.</summary>
	internal UiValue<UiLength> Value { get; }

	/// <summary>Whether this property was set at all.</summary>
	public bool IsDeclared => Value.IsDeclared;

	/// <summary>A bare fraction of the widget basis.</summary>
	public static implicit operator UiSize(double basis) => Of(UiLength.OfBasis(basis));

	/// <summary>An already-built length.</summary>
	public static implicit operator UiSize(UiLength length) => Of(length);

	/// <summary>An already-wrapped value, so <c>UiValue.From(() =&gt; …)</c> call sites keep working.</summary>
	public static implicit operator UiSize(UiValue<UiLength> value) => new(value);

	/// <summary>A fraction of the widget basis, for a call site that cannot rely on the implicit
	/// conversion.</summary>
	public static UiSize FromBasis(double basis) => Of(UiLength.OfBasis(basis));

	/// <summary>A fraction clamped against the containing stack's cross extent.</summary>
	public static UiSize FromBasis(double basis, double maxOfCross)
		=> Of(UiLength.OfBasis(basis, maxOfCross));

	/// <summary>A fraction that stops growing at <paramref name="referenceExtent" /> - see
	/// <see cref="UiLength.Capped" />.</summary>
	public static UiSize Capped(double basis, double referenceExtent)
		=> Of(UiLength.Capped(basis, referenceExtent));

	/// <summary>A constant length, for a call site that cannot rely on the implicit conversion.</summary>
	public static UiSize Of(UiLength length) => new(UiValue.Of(length));

	/// <summary>A length computed on each evaluation.</summary>
	public static UiSize From(Func<UiLength> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiSize(UiValue.From(compute));
	}

	/// <summary>A length that may evaluate to absent.</summary>
	public static UiSize Optional(Func<UiSize> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiSize(UiValue.Optional(() => compute().Value));
	}

	/// <summary>The absent value.</summary>
	public static UiSize None() => default;

	/// <summary>Evaluates the property.</summary>
	/// <returns><c>false</c> when the property is absent.</returns>
	public bool TryEvaluate(out UiLength length) => Value.TryEvaluate(out length!);
}
