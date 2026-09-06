namespace MacroDeck.Localization.Compiler;

/// <summary>A parameter type declared in a resource comment.</summary>
internal sealed class LocalizationParameterDeclaration
{
	public LocalizationParameterDeclaration(string name,
		string declaredType,
		bool isKnownType,
		LocalizationParameterType type)
	{
		Name = name;
		DeclaredType = declaredType;
		IsKnownType = isKnownType;
		Type = type;
	}

	/// <summary>The placeholder this declares the type of.</summary>
	public string Name { get; }

	/// <summary>The type name exactly as written, for the MDLOC004 message.</summary>
	public string DeclaredType { get; }

	/// <summary>Whether <see cref="DeclaredType" /> is one the compiler can emit.</summary>
	public bool IsKnownType { get; }

	/// <summary>The resolved type; meaningless unless <see cref="IsKnownType" />.</summary>
	public LocalizationParameterType Type { get; }
}

/// <summary>
/// Reads parameter declarations out of a resource comment. A declaration is a bracketed prefix such as
/// <c>[count:int]</c>, and anything after the last one stays the translator note.
/// </summary>
/// <remarks>
/// The comment field is where this has to live: <c>.resx</c> has no other per-entry metadata, and it is
/// the field Rider's Localization Manager shows beside the value. Confining declarations to a leading
/// bracketed run keeps the rest of the field usable for the note it is meant to hold.
/// </remarks>
internal static class LocalizationParameterDeclarations
{
	/// <summary>The marker retiring a key, read from the same bracketed prefix run.</summary>
	public const string RemovedMarker = "removed";

	/// <summary>The guidance in a <c>[removed:…]</c> prefix, or <c>null</c> when the key is current.</summary>
	public static string? ParseRemoval(string? comment)
	{
		foreach (var declaration in ParseAll(comment))
		{
			if (string.Equals(declaration.Name, RemovedMarker, StringComparison.Ordinal))
			{
				return declaration.DeclaredType.Length > 0 ? declaration.DeclaredType : "This key has been removed.";
			}
		}

		return null;
	}

	/// <summary>Whether <paramref name="comment" /> declares the entry to be one form of a plural family.</summary>
	public static bool DeclaresPlural(string? comment)
	{
		foreach (var declaration in ParseAll(comment))
		{
			if (string.Equals(declaration.Name, LocalizationPluralForms.Marker, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Parses the declarations prefixing <paramref name="comment" />.</summary>
	public static List<LocalizationParameterDeclaration> Parse(string? comment)
	{
		var declarations = new List<LocalizationParameterDeclaration>();

		foreach (var declaration in ParseAll(comment))
		{
			if (!IsMarker(declaration.Name))
			{
				declarations.Add(declaration);
			}
		}

		return declarations;
	}

	private static bool IsMarker(string name)
		=> string.Equals(name, RemovedMarker, StringComparison.Ordinal) ||
			string.Equals(name, LocalizationPluralForms.Marker, StringComparison.Ordinal);

	private static bool IsBareMarker(string body)
		=> string.Equals(body.Trim(), LocalizationPluralForms.Marker, StringComparison.Ordinal);

	private static List<LocalizationParameterDeclaration> ParseAll(string? comment)
	{
		var declarations = new List<LocalizationParameterDeclaration>();

		if (string.IsNullOrEmpty(comment))
		{
			return declarations;
		}

		var index = 0;
		var text = comment!;

		while (index < text.Length)
		{
			while (index < text.Length && char.IsWhiteSpace(text[index]))
			{
				index++;
			}

			if (index >= text.Length || text[index] != '[')
			{
				break;
			}

			var closing = text.IndexOf(']', index + 1);
			if (closing < 0)
			{
				break;
			}

			var body = text.Substring(index + 1, closing - index - 1);
			var separator = body.IndexOf(':');

			if (separator <= 0)
			{
				// A bare marker carries no value. Only the markers this compiler knows are consumed:
				// any other bracketed word stays part of the translator note, exactly as before.
				if (!IsBareMarker(body))
				{
					break;
				}

				declarations.Add(new LocalizationParameterDeclaration(body.Trim(),
					string.Empty,
					false,
					LocalizationParameterType.String));
				index = closing + 1;
				continue;
			}

			var name = body.Substring(0, separator).Trim();
			var declaredType = body.Substring(separator + 1).Trim();

			if (!LocalizationTemplate.IsPlaceholderName(name))
			{
				break;
			}

			var isKnown = LocalizationParameterTypes.TryParse(declaredType, out var type);
			declarations.Add(new LocalizationParameterDeclaration(name, declaredType, isKnown, type));
			index = closing + 1;
		}

		return declarations;
	}
}
