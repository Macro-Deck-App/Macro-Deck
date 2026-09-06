using MacroDeck.Localization;

namespace MacroDeck.Ui.Config.Validation;

/// <summary>
/// One author-supplied condition a field's value has to satisfy, with the message shown when it does not.
/// The declared constraints - required, a pattern, a length, a numeric bound - cover what the existing action
/// parameter schema can express; a rule covers what it cannot, which is anything needing more than one field
/// or more than one comparison.
///
/// <para>
/// <see cref="IsSatisfied" /> reads whatever the author wants, including reactive state. Reading a
/// <see cref="Runtime.UiState{T}" /> inside it makes the rule re-evaluate when that state changes, exactly
/// as a value provider does, because it is invoked from the same kind of cell.
/// </para>
/// </summary>
public sealed record UiValidationRule
{
	/// <summary>What the field shows while this rule is unsatisfied.</summary>
	public required LocalizedText Message { get; init; }

	/// <summary>Whether the rule holds. Invoked on every validation, so it stays a pure read.</summary>
	public required Func<bool> IsSatisfied { get; init; }

	/// <summary>Builds a rule from its message and condition.</summary>
	public static UiValidationRule Require(LocalizedText message, Func<bool> isSatisfied)
	{
		if (message.IsEmpty)
		{
			throw new ArgumentException("A rule needs a message.", nameof(message));
		}

		ArgumentNullException.ThrowIfNull(isSatisfied);

		return new UiValidationRule { Message = message, IsSatisfied = isSatisfied };
	}
}
