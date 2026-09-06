using System.Globalization;
using System.Text;

namespace MacroDeck.Localization.Compiler;

/// <summary>
/// Parsing and formatting of a localization template such as <c>Connected as {userName}</c>. One
/// implementation serves the source generator, the runtime resolver and - mirrored line for line - the
/// TypeScript formatter, so all three agree on what a placeholder is and what a value looks like once
/// substituted.
/// </summary>
internal static class LocalizationTemplate
{
	/// <summary>Collects the placeholder names in <paramref name="template" />, in first-appearance order,
	/// without duplicates. Malformed braces yield no placeholder rather than an exception.</summary>
	public static List<string> ParsePlaceholders(string? template)
	{
		var names = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		if (string.IsNullOrEmpty(template))
		{
			return names;
		}

		var index = 0;
		while (index < template!.Length)
		{
			var character = template[index];

			if (character == '{')
			{
				if (index + 1 < template.Length && template[index + 1] == '{')
				{
					index += 2;
					continue;
				}

				var closing = template.IndexOf('}', index + 1);
				if (closing > index + 1)
				{
					var name = template.Substring(index + 1, closing - index - 1);
					if (IsPlaceholderName(name) && seen.Add(name))
					{
						names.Add(name);
					}

					index = closing + 1;
					continue;
				}
			}

			index++;
		}

		return names;
	}

	/// <summary>Substitutes <paramref name="arguments" /> into <paramref name="template" />. An argument
	/// with no value is left as its literal placeholder, which is visible in the UI on purpose - a blank
	/// where a device name belongs reads as a rendering bug, <c>{deviceName}</c> reads as a missing
	/// argument.</summary>
	public static string Format(string? template, IReadOnlyDictionary<string, object?>? arguments)
	{
		if (string.IsNullOrEmpty(template))
		{
			return string.Empty;
		}

		var builder = new StringBuilder(template!.Length);
		var index = 0;

		while (index < template.Length)
		{
			var character = template[index];

			if (character == '{' && index + 1 < template.Length && template[index + 1] == '{')
			{
				builder.Append('{');
				index += 2;
				continue;
			}

			if (character == '}' && index + 1 < template.Length && template[index + 1] == '}')
			{
				builder.Append('}');
				index += 2;
				continue;
			}

			if (character == '{')
			{
				var closing = template.IndexOf('}', index + 1);
				if (closing > index + 1)
				{
					var name = template.Substring(index + 1, closing - index - 1);
					object? value = null;

					if (IsPlaceholderName(name) &&
						arguments != null &&
						arguments.TryGetValue(name, out value))
					{
						builder.Append(ToInvariantText(value));
					}
					else
					{
						builder.Append(template, index, closing - index + 1);
					}

					index = closing + 1;
					continue;
				}
			}

			builder.Append(character);
			index++;
		}

		return builder.ToString();
	}

	/// <summary>Whether <paramref name="name" /> is a usable placeholder name - the same rule the
	/// generator uses to turn a placeholder into a C# parameter.</summary>
	public static bool IsPlaceholderName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		var first = name![0];
		if (first != '_' && !char.IsLetter(first))
		{
			return false;
		}

		for (var index = 1; index < name.Length; index++)
		{
			var character = name[index];
			if (character != '_' && !char.IsLetterOrDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// The one text form an argument takes once substituted, chosen so the TypeScript mirror produces the
	/// same characters: no thousands separators, no culture-dependent decimal mark, lowercase booleans,
	/// and a lowercase exponent so <c>1e+21</c> matches what JavaScript's own number-to-string produces.
	/// </summary>
	public static string ToInvariantText(object? value)
	{
		switch (value)
		{
			case null:
				return string.Empty;
			case string text:
				return text;
			case bool flag:
				return flag ? "true" : "false";
			case double number:
				return number.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
			case float number:
				return number.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
			case IFormattable formattable:
				return formattable.ToString(null, CultureInfo.InvariantCulture);
			default:
				return value.ToString() ?? string.Empty;
		}
	}
}
