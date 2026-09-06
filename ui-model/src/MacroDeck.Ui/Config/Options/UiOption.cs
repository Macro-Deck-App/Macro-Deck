using MacroDeck.Localization;

namespace MacroDeck.Ui.Config.Options;

/// <summary>
/// One option a selection control offers. The counterpart of the existing action parameter option, down to
/// the label falling back to the value and the metadata map a consumer that does not know a key ignores.
/// </summary>
public sealed record UiOption
{
	/// <summary>What the field stores when this option is picked.</summary>
	public required string Value { get; init; }

	/// <summary>What the user reads. Absent means the control shows <see cref="Value" />. Localizable, so a
	/// selection control's items translate along with the field that holds them.</summary>
	public LocalizedText Label { get; init; }

	/// <summary>A short marker the control draws beside the label, saying something about this option's
	/// standing rather than what it is - the Action Button's state list marking which state the button is
	/// on right now. A word, not a sentence: a renderer with no room for it leaves it out.</summary>
	public LocalizedText Badge { get; init; }

	/// <summary>A sentence saying what picking this option means, for a choice a renderer draws as cards
	/// rather than as a list - the shape the original Music Player editor's cover-style picker had, where
	/// each option carried a name and a line under it. A renderer with no room for it shows only
	/// <see cref="Label" />.</summary>
	public LocalizedText Description { get; init; }

	/// <summary>An icon name a segmented choice renders instead of - or alongside - the label, for a choice
	/// that is naturally a row of icon buttons rather than text (an alignment or position picker). A renderer
	/// that does not draw icons ignores it and falls back to <see cref="Label" /> or <see cref="Value" />.
	/// </summary>
	public string? Icon { get; init; }

	/// <summary>Additive per-option information a renderer that knows a key can use - an icon name, a
	/// grouping. A renderer that does not know a key ignores it, which is what keeps adding one compatible.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Metadata { get; init; }

	/// <summary>An option whose label is its value.</summary>
	public static UiOption Of(string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(value);

		return new UiOption { Value = value };
	}

	/// <summary>An option with a distinct label.</summary>
	public static UiOption Of(string value, LocalizedText label)
	{
		ArgumentException.ThrowIfNullOrEmpty(value);

		if (label.IsEmpty)
		{
			throw new ArgumentException("An option label must not be empty; use Of(value) instead.",
				nameof(label));
		}

		return new UiOption { Value = value, Label = label };
	}
}
