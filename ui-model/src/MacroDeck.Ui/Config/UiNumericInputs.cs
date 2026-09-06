using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// A number field. Counterpart of the existing <c>Number</c> parameter type, which the editor renders as a
/// number box and, with the slider flag set, as a range control alongside it.
///
/// <para>
/// <b>A slider is this primitive, not a second one.</b> The existing schema has no slider type: its slider
/// factory produces a <c>Number</c> parameter with the slider flag set, and the editor's number control
/// switches on the flag. A <c>slider</c> type here would be a member the enum this vocabulary was derived from
/// does not have.
/// </para>
/// </summary>
public sealed record UiNumberInput : UiInput<double>
{
	/// <summary>The lowest accepted value. Also the bound validation checks.</summary>
	public UiValue<double> Min { get; init; }

	/// <summary>The highest accepted value. Also the bound validation checks.</summary>
	public UiValue<double> Max { get; init; }

	/// <summary>What the control increments by.</summary>
	public UiValue<double> Step { get; init; }

	/// <summary>Whether the control renders a slider alongside its box.</summary>
	public UiValue<bool> ShowSlider { get; init; }

	/// <summary>
	/// The values offered, drawing the field as a list to pick from instead of a box to type in - a
	/// presentation flag over the same value, like <see cref="ShowSlider" />, rather than a second node
	/// type. For a short, closed range of numbers that each read better as a phrase than as a digit: the
	/// Weather widget's forecast length, offered as "5 days" rather than as a spinner at 5.
	///
	/// <para>
	/// Each option carries its number as <see cref="Options.UiOption.Value" />'s decimal text, since an
	/// option's value is a string everywhere in this profile. The value on the wire and in the stored
	/// configuration stays a number, which is what keeps this a presentation choice: a renderer that
	/// ignores the list still edits the same numeric field.
	/// </para>
	/// </summary>
	public UiValue<IReadOnlyList<UiOption>> Options { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Number;

	/// <inheritdoc />
	protected internal override UiValue<double> MinConstraint => Min;

	/// <inheritdoc />
	protected internal override UiValue<double> MaxConstraint => Max;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Min, Min);
		properties.Set(UiConfigProperties.Max, Max);
		properties.Set(UiConfigProperties.Step, Step);
		properties.Set(UiConfigProperties.ShowSlider, ShowSlider);
		properties.Set(UiConfigProperties.Options, Options);
	}
}

/// <summary>
/// A duration, in milliseconds. Counterpart of the <c>Duration</c> parameter type, which the editor renders as
/// its duration input and validates against the same numeric bounds a number field uses - which is why the
/// unit is milliseconds rather than a <see cref="TimeSpan" />: the bound and the value have to be comparable
/// on the wire, and the existing schema already carries both as numbers.
/// </summary>
public sealed record UiDurationInput : UiInput<double>
{
	/// <summary>The shortest accepted duration, in milliseconds. Also the bound validation checks.</summary>
	public UiValue<double> Min { get; init; }

	/// <summary>The longest accepted duration, in milliseconds. Also the bound validation checks.</summary>
	public UiValue<double> Max { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Duration;

	/// <inheritdoc />
	protected internal override UiValue<double> MinConstraint => Min;

	/// <inheritdoc />
	protected internal override UiValue<double> MaxConstraint => Max;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Min, Min);
		properties.Set(UiConfigProperties.Max, Max);
	}
}
