using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>A single selection from a fixed list. Counterpart of the existing <c>Choice</c> parameter type,
/// which the editor renders as a select clearable while the field is optional.</summary>
public sealed record UiChoiceInput : UiOptionsInput<string>
{
	/// <summary>Draws the options as a segmented control instead of a select - a presentation flag, not a
	/// second node type, the same way <see cref="UiNumberInput.ShowSlider" /> and
	/// <see cref="UiStringInput.Multiline" /> are. Counterpart of the original Action Button appearance
	/// form's Single state / Multi state switch.</summary>
	public UiValue<bool> Segmented { get; init; }

	/// <summary>Draws the options as cards, each showing its label above its own
	/// <see cref="Options.UiOption.Description" />, instead of as a select - the same kind of presentation
	/// flag as <see cref="Segmented" />, for a choice whose options need a sentence each rather than a word.
	/// </summary>
	public UiValue<bool> Cards { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Choice;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Segmented, Segmented);
		properties.Set(UiConfigProperties.Cards, Cards);
	}
}

/// <summary>
/// A single selection whose options the host resolves at edit time. Counterpart of the <c>DynamicChoice</c>
/// parameter type, which the editor renders as a select that fetches when it opens and carries a reload button
/// next to it - which is why <c>open</c> and <c>reload</c> are in the event vocabulary.
/// </summary>
public sealed record UiDynamicChoiceInput : UiOptionsInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.DynamicChoice;
}

/// <summary>
/// A text field with suggestions. Counterpart of the <c>Autocomplete</c> parameter type, which the editor
/// renders as a combobox filtering locally over shipped options and refetching for a dynamic list - which is
/// why <c>filter</c> is in the event vocabulary and why a custom value is a property rather than an
/// assumption.
/// </summary>
public sealed record UiAutocompleteInput : UiOptionsInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Autocomplete;
}

/// <summary>
/// A multiple selection. Counterpart of the <c>MultiSelect</c> parameter type. The value is a list, so it is
/// authored with <see cref="Dsl.UiValue.Of{T}" />: C# forbids a user-defined conversion whose source is an
/// interface type, so the implicit conversion is unavailable for exactly this case.
/// </summary>
public sealed record UiMultiSelectInput : UiOptionsInput<IReadOnlyList<string>>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.MultiSelect;
}

/// <summary>
/// Picks the widget an action acts on. Counterpart of the <c>WidgetTarget</c> parameter type, which the editor
/// renders as its widget picker and whose options always come from a host-side source.
/// </summary>
public sealed record UiWidgetTargetInput : UiOptionsInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.WidgetTarget;
}
