using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using MacroDeck.Localization;

namespace MacroDeck.Ui.Config.Validation;

/// <summary>
/// One input's constraints and current value, and the rules that decide whether it is acceptable. A
/// deliberate port of the existing action flow validation - same checks, same order, same messages - so a
/// field authored through this DSL is accepted or rejected exactly as the same field authored as an action
/// parameter is. A second, subtly different validator would let a plugin's own preflight disagree with the
/// editor that gates saving.
///
/// <para>
/// <b>The order matters and is the existing one.</b> An invisible field is skipped entirely. Then emptiness,
/// which short-circuits: an empty required field says "is required" and nothing else, so the user is not told
/// that empty is also not a valid URL. Then, for a non-blank text value, the pattern, the length, and the
/// per-type format check. Then the numeric bounds. Then the author's own rules, last, because a rule that
/// assumes a well-formed value should not have to re-check that it is one.
/// </para>
///
/// <para>
/// <b>Deliberate differences from the editor's copy.</b> A field with no <see cref="Required" /> flag is
/// optional here, where the editor's historical rule treats a missing flag on a built-in block as required -
/// that rule exists to keep parameters authored before the flag was introduced working, and nothing was
/// authored against this DSL before it existed. The editor also skips format checks for a value carrying a
/// variable reference or a Liquid template; both are the action editor's variable binding, which is not part
/// of this profile.
/// </para>
/// </summary>
public sealed record UiValidation
{
	/// <summary>The primitive's type string - one of <see cref="UiConfigPrimitives" /> - which decides the
	/// format check and whether emptiness applies at all.</summary>
	public required string Type { get; init; }

	/// <summary>The field name, used in a message when there is no <see cref="Label" />. An input's name is
	/// its node id, which is the name it submits as.</summary>
	public required string Name { get; init; }

	/// <summary>The field's caption, preferred over <see cref="Name" /> in a message. Itself localizable,
	/// so a message built around it does not end up half-translated.</summary>
	public LocalizedText Label { get; init; }

	/// <summary>The current value, or <c>null</c> when absent. A <see cref="string" /> for a text-shaped
	/// primitive and a <see cref="double" /> for a numeric one; any other shape is checked for emptiness
	/// only, because there is no format to check it against.</summary>
	public object? Value { get; init; }

	/// <summary>Whether an empty value is rejected.</summary>
	public bool Required { get; init; }

	/// <summary>
	/// Whether the field is currently shown. A hidden field is skipped: it keeps its value and is still
	/// submitted, so a required field the user cannot see must never be able to block a submit.
	/// </summary>
	public bool Visible { get; init; } = true;

	/// <summary>A pattern the value has to match. A pattern that does not compile, or one that takes too long
	/// on this value, is ignored rather than blocking the user: it came from the plugin, not from them.
	/// </summary>
	public string? ValidationRegex { get; init; }

	/// <summary>The longest accepted value, in characters.</summary>
	public int? MaxLength { get; init; }

	/// <summary>The lowest accepted number, for a numeric primitive.</summary>
	public double? Min { get; init; }

	/// <summary>The highest accepted number, for a numeric primitive.</summary>
	public double? Max { get; init; }

	/// <summary>Extra conditions, checked in order after everything above.</summary>
	public IReadOnlyList<UiValidationRule> Rules { get; init; } = [];

	/// <summary>Runs the checks in the documented order and returns the first failure.</summary>
	public UiValidationOutcome Validate()
	{
		if (!Visible)
		{
			return UiValidationOutcome.Valid();
		}

		var caption = Label.IsEmpty ? LocalizedText.FromLiteral(Name) : Label;

		if (Required && IsEmpty())
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.Required(caption));
		}

		if (Value is string text && text.Trim().Length > 0)
		{
			if (FormatFailure(caption, text) is { } textFailure)
			{
				return textFailure;
			}
		}

		if (Value is double number && BoundFailure(caption, number) is { } numberFailure)
		{
			return numberFailure;
		}

		foreach (var rule in Rules)
		{
			if (!rule.IsSatisfied())
			{
				return UiValidationOutcome.Invalid(rule.Message);
			}
		}

		return UiValidationOutcome.Valid();
	}

	/// <summary>Whether <paramref name="value" /> parses as a URL the existing editor accepts: an absolute
	/// URL whose HTTP or HTTPS form was spelled with the full <c>//</c>, so <c>https:/example.com</c> is
	/// rejected rather than silently normalised.</summary>
	public static bool IsValidUrl(string value)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
		{
			return false;
		}

		if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Whether <paramref name="value" /> is a dotted-quad IPv4 address or a colon-bearing
	/// hexadecimal IPv6 one - the same two shapes the existing editor accepts.</summary>
	public static bool IsValidIpAddress(string value)
	{
		ArgumentNullException.ThrowIfNull(value);

		return IsIpV4(value) || IsIpV6(value);
	}

	/// <summary>Whether <paramref name="value" /> parses as JSON.</summary>
	public static bool IsValidJson(string value)
	{
		try
		{
			using var document = JsonDocument.Parse(value);

			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>Whether the current value counts as empty: absent, blank text, or a number that is not a
	/// number. A boolean primitive is never empty, because false is an answer.</summary>
	private bool IsEmpty()
	{
		if (string.Equals(Type, UiConfigPrimitives.Boolean, StringComparison.Ordinal))
		{
			return false;
		}

		return Value switch
		{
			null => true,
			string text => text.Trim().Length == 0,
			double number => double.IsNaN(number),
			_ => false,
		};
	}

	private UiValidationOutcome? FormatFailure(LocalizedText caption, string text)
	{
		if (!string.IsNullOrEmpty(ValidationRegex) && !MatchesPattern(text))
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.PatternMismatch(caption));
		}

		if (MaxLength is { } maxLength && maxLength > 0 && text.Length > maxLength)
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.TooLong(caption, maxLength));
		}

		if (string.Equals(Type, UiConfigPrimitives.Url, StringComparison.Ordinal) && !IsValidUrl(text))
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.InvalidUrl(caption));
		}

		if (string.Equals(Type, UiConfigPrimitives.IpAddress, StringComparison.Ordinal) && !IsValidIpAddress(text))
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.InvalidIpAddress(caption));
		}

		if (string.Equals(Type, UiConfigPrimitives.Json, StringComparison.Ordinal) && !IsValidJson(text))
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.InvalidJson(caption));
		}

		return null;
	}

	/// <summary>The numeric bounds, which apply to the two numeric primitives only - a bound on a colour or a
	/// hotkey means nothing, and the existing editor checks them for exactly these two.</summary>
	private UiValidationOutcome? BoundFailure(LocalizedText caption, double number)
	{
		if (!string.Equals(Type, UiConfigPrimitives.Number, StringComparison.Ordinal) &&
			!string.Equals(Type, UiConfigPrimitives.Duration, StringComparison.Ordinal))
		{
			return null;
		}

		if (Min is { } min && number < min)
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.AtLeast(caption, min));
		}

		if (Max is { } max && number > max)
		{
			return UiValidationOutcome.Invalid(MacroDeckStrings.Validation.AtMost(caption, max));
		}

		return null;
	}

	private bool MatchesPattern(string text)
	{
		try
		{
			return Regex.IsMatch(text, ValidationRegex!, RegexOptions.None, _patternTimeout);
		}
		catch (ArgumentException)
		{
			// A pattern the plugin got wrong must not make the field unfillable.
			return true;
		}
		catch (RegexMatchTimeoutException)
		{
			return true;
		}
	}

	private static bool IsIpV4(string value)
	{
		var parts = value.Split('.');

		if (parts.Length != 4)
		{
			return false;
		}

		foreach (var part in parts)
		{
			if (part.Length is 0 or > 3)
			{
				return false;
			}

			foreach (var character in part)
			{
				if (!char.IsAsciiDigit(character))
				{
					return false;
				}
			}

			if (int.Parse(part, CultureInfo.InvariantCulture) > 255)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsIpV6(string value)
	{
		if (!value.Contains(':', StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var character in value)
		{
			if (character != ':' && !char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>How long an author-supplied pattern may run before it is treated as unusable. A pattern from
	/// a plugin is untrusted input, and a catastrophically backtracking one would otherwise hang the
	/// evaluation that renders the field.</summary>
	private static readonly TimeSpan _patternTimeout = TimeSpan.FromSeconds(1);
}
