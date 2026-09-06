using MacroDeck.Localization;

namespace MacroDeck.Ui.Config.Validation;

/// <summary>
/// What validating one input's value produced: valid, or invalid with the message the field shows. One
/// message rather than a list, because that is what the existing editor renders per field and what the
/// <c>validationMessage</c> property carries; a rule that has more to say says it in its own message.
/// </summary>
public readonly record struct UiValidationOutcome
{
	private UiValidationOutcome(bool isValid, LocalizedText message)
	{
		IsValid = isValid;
		Message = message;
	}

	/// <summary>Whether the value satisfies every declared constraint and rule.</summary>
	public bool IsValid { get; }

	/// <summary>Why the value was rejected, absent when it was not. Never empty when <see cref="IsValid" />
	/// is false - a field marked invalid with nothing to say is a dead end for the user. Carried as a
	/// reference rather than resolved text so the message renders in the reader's language.</summary>
	public LocalizedText Message { get; }

	/// <summary>The valid outcome. Also what <c>default</c> is <b>not</b>: an outcome has to be produced by
	/// <see cref="Valid" /> or <see cref="Invalid" />, so a forgotten assignment reads as invalid with no
	/// message rather than silently passing.</summary>
	public static UiValidationOutcome Valid() => new(isValid: true, default);

	/// <summary>An invalid outcome carrying <paramref name="message" />.</summary>
	public static UiValidationOutcome Invalid(LocalizedText message)
	{
		if (message.IsEmpty)
		{
			throw new ArgumentException("An invalid outcome needs a message.", nameof(message));
		}

		return new UiValidationOutcome(isValid: false, message);
	}
}
