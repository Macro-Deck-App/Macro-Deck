using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Components;

/// <summary>
/// Draws one of several layouts, chosen by the reader from the box this node is given: its
/// <see cref="Variants" /> in order, the first whose condition holds, else <see cref="Default" />. The choice
/// is made where the size is known and is made again on every resize, with no round-trip to the view.
///
/// <para>
/// <b>Wire form.</b> A <see cref="UiComponents.Responsive" /> node whose first child is <see cref="Default" />
/// and whose remaining children are the variants' contents in order; its
/// <see cref="UiComponentProperties.Variants" /> property holds one condition object per variant, index-aligned
/// with the children after the first. The chosen child is drawn across the whole box.
/// </para>
///
/// <para>
/// <b>Every variant is built and kept current.</b> All of them are materialized and patched whichever one is
/// on screen, so each counts toward the tree's node and byte limits. A reader that does not know the type
/// draws <see cref="UiElement.Fallback" />; when none is set, <see cref="Default" /> is emitted a second time as
/// the fallback, under ids beneath <c>&lt;id&gt;._fallback</c>, which is why the key <c>_fallback</c> is
/// reserved for the variants and why an input cannot sit inside <see cref="Default" /> without an explicit
/// fallback.
/// </para>
///
/// <para>
/// <see cref="UiComponentContainer.MainSize" />, <see cref="UiComponentContainer.Fill" />,
/// <see cref="UiComponentContainer.ColumnSpan" /> and <see cref="UiComponentContainer.RowSpan" /> belong on
/// this node; a variant's content or <see cref="Default" /> that sets them is rejected, since the chosen child
/// always gets the whole box. <see cref="UiContainer.Children" /> is not used and must stay empty.
/// </para>
/// </summary>
public sealed record UiResponsive : UiComponentContainer
{
	/// <summary>The layout drawn when no variant's condition holds, and by a reader that cannot tell which
	/// does.</summary>
	public required UiElement Default
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>The conditional layouts, tried in order. Empty means <see cref="Default" /> is always
	/// drawn.</summary>
	public IReadOnlyList<UiResponsiveVariant> Variants
	{
		get;
		init => field = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value];
	} = [];

	/// <inheritdoc />
	public override string Type => UiComponents.Responsive;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		IReadOnlyList<UiResponsiveCondition> conditions = [.. Variants.Select(UiResponsiveCondition.Of)];
		properties.Set(UiComponentProperties.Variants, UiValue.Of(conditions));
	}
}

internal sealed record UiResponsiveCondition(
	[property: JsonPropertyOrder(0)] double? MinWidth,
	[property: JsonPropertyOrder(1)] double? MaxWidth,
	[property: JsonPropertyOrder(2)] double? MinHeight,
	[property: JsonPropertyOrder(3)] double? MaxHeight,
	[property: JsonPropertyOrder(4)] double? MinAspect,
	[property: JsonPropertyOrder(5)] double? MaxAspect)
{
	internal static UiResponsiveCondition Of(UiResponsiveVariant variant)
		=> new(variant.MinWidth,
			variant.MaxWidth,
			variant.MinHeight,
			variant.MaxHeight,
			variant.MinAspect,
			variant.MaxAspect);
}

/// <summary>
/// One conditional layout of a <see cref="UiResponsive" />: its <see cref="Content" /> is drawn when every
/// assigned bound holds for the box the node is given. Widths and heights are in deck cells
/// (<see cref="UiLength.Cell" /> reference units each); aspects are width over height. A minimum holds when the
/// extent is at least that value and a maximum when it is below it, so adjacent variants split a range without
/// overlap. A bound on an extent the reader does not know never holds. No bound assigned always holds.
///
/// <para>
/// On a deck tile a cell is one grid cell, so a 2x1 tile is two cells plus the gap wide. Anywhere else, such
/// as a folder view or a dialog, a cell is 120 of the reader's own layout units (CSS pixels in Macro Deck's
/// clients).
/// </para>
/// </summary>
public sealed record UiResponsiveVariant
{
	/// <summary>What this variant draws. A single element, not a conditional, a repeat or a fragment.</summary>
	public required UiElement Content
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>The narrowest box, in cells, this variant is drawn in.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
	public double? MinWidth
	{
		get;
		init => field = UiResponsiveSelection.Extent(value, nameof(MinWidth));
	}

	/// <summary>The width, in cells, from which this variant is no longer drawn.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
	public double? MaxWidth
	{
		get;
		init => field = UiResponsiveSelection.Extent(value, nameof(MaxWidth));
	}

	/// <summary>The shortest box, in cells, this variant is drawn in.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
	public double? MinHeight
	{
		get;
		init => field = UiResponsiveSelection.Extent(value, nameof(MinHeight));
	}

	/// <summary>The height, in cells, from which this variant is no longer drawn.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
	public double? MaxHeight
	{
		get;
		init => field = UiResponsiveSelection.Extent(value, nameof(MaxHeight));
	}

	/// <summary>The smallest width over height this variant is drawn at. <c>1.5</c> means clearly
	/// landscape.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number above zero.</exception>
	public double? MinAspect
	{
		get;
		init => field = UiResponsiveSelection.Aspect(value, nameof(MinAspect));
	}

	/// <summary>The width over height from which this variant is no longer drawn. <c>0.67</c> means clearly
	/// portrait.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a finite number above zero.</exception>
	public double? MaxAspect
	{
		get;
		init => field = UiResponsiveSelection.Aspect(value, nameof(MaxAspect));
	}
}

/// <summary>
/// The rule every reader applies to a <see cref="UiComponents.Responsive" /> node, over its wire form. Public so
/// a reader, a test host or a host answering a hardware press for a tile chooses exactly as a renderer does.
/// </summary>
public static class UiResponsiveSelection
{
	/// <summary>
	/// The slack, in cells and in aspect, every bound is compared with: a minimum holds from
	/// <c>min - Tolerance</c> and a maximum stops holding from <c>max - Tolerance</c>. It absorbs the rounding of
	/// a box computed from scaled pixels, so a tile that is exactly two cells wide counts as two cells on every
	/// reader.
	/// </summary>
	public const double Tolerance = 0.0001;

	/// <summary>
	/// The index of the child a reader draws for a box. <c>0</c> is the default; <c>i + 1</c> is the variant
	/// whose condition is <paramref name="variants" />[<c>i</c>].
	/// </summary>
	/// <param name="variants">The node's <see cref="UiComponentProperties.Variants" /> property. Anything but an
	/// array means no variants.</param>
	/// <param name="childCount">How many children the node has. A condition with no child to pair with is
	/// skipped.</param>
	/// <param name="widthCells">The box's width in cells, or <c>null</c> when unknown. Zero, negative and
	/// non-finite widths count as unknown.</param>
	/// <param name="heightCells">The box's height in cells, with the same rules.</param>
	/// <returns>The child index, or <c>-1</c> when the node has no children at all. A condition that is not an
	/// object never holds; a member that is not a number is ignored.</returns>
	public static int SelectChild(JsonElement variants, int childCount, double? widthCells, double? heightCells)
	{
		if (childCount <= 0)
		{
			return -1;
		}

		if (variants.ValueKind != JsonValueKind.Array)
		{
			return 0;
		}

		var width = Known(widthCells);
		var height = Known(heightCells);
		var index = 0;

		foreach (var condition in variants.EnumerateArray())
		{
			if (index + 1 >= childCount)
			{
				break;
			}

			if (Holds(condition, width, height))
			{
				return index + 1;
			}

			index++;
		}

		return 0;
	}

	private static bool Holds(JsonElement condition, double? width, double? height)
	{
		if (condition.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		var aspect = width is not null && height is not null ? width / height : null;

		return Min(condition, "minWidth", width) &&
			Max(condition, "maxWidth", width) &&
			Min(condition, "minHeight", height) &&
			Max(condition, "maxHeight", height) &&
			Min(condition, "minAspect", aspect) &&
			Max(condition, "maxAspect", aspect);
	}

	private static bool Min(JsonElement condition, string name, double? value)
		=> Bound(condition, name) is not { } bound || (value is { } v && v >= bound - Tolerance);

	private static bool Max(JsonElement condition, string name, double? value)
		=> Bound(condition, name) is not { } bound || (value is { } v && v < bound - Tolerance);

	private static double? Bound(JsonElement condition, string name)
		=> condition.TryGetProperty(name, out var member) &&
			member.ValueKind == JsonValueKind.Number &&
			member.TryGetDouble(out var bound) &&
			double.IsFinite(bound)
				? bound
				: null;

	private static double? Known(double? extent)
		=> extent is { } value && double.IsFinite(value) && value > 0 ? value : null;

	internal static double? Extent(double? value, string name)
		=> value is null || (double.IsFinite(value.Value) && value.Value >= 0)
			? value
			: throw new ArgumentOutOfRangeException(name, value, "A width or height must be finite and not negative.");

	internal static double? Aspect(double? value, string name)
		=> value is null || (double.IsFinite(value.Value) && value.Value > 0)
			? value
			: throw new ArgumentOutOfRangeException(name, value, "An aspect must be a finite number above zero.");
}
