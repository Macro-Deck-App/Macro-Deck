namespace MacroDeck.Localization.Compiler;

/// <summary>The argument types a localization placeholder may declare.</summary>
/// <remarks>Deliberately small. Every member has one unambiguous invariant text form, which is what lets
/// the C# and TypeScript formatters agree character for character without either side reaching for
/// culture-dependent number or date formatting.</remarks>
internal enum LocalizationParameterType
{
	/// <summary>Substituted verbatim.</summary>
	String,

	/// <summary>A 32-bit integer.</summary>
	Int,

	/// <summary>A 64-bit integer.</summary>
	Long,

	/// <summary>A double-precision number.</summary>
	Double,

	/// <summary><c>true</c> or <c>false</c>.</summary>
	Bool,
}

/// <summary>Maps between <see cref="LocalizationParameterType" /> and the names used in resources and code.</summary>
internal static class LocalizationParameterTypes
{
	/// <summary>Parses a type name as written in a resource comment, for example <c>int</c>.</summary>
	public static bool TryParse(string? declared, out LocalizationParameterType type)
	{
		switch (declared)
		{
			case "string":
				type = LocalizationParameterType.String;
				return true;
			case "int":
				type = LocalizationParameterType.Int;
				return true;
			case "long":
				type = LocalizationParameterType.Long;
				return true;
			case "double":
				type = LocalizationParameterType.Double;
				return true;
			case "bool":
				type = LocalizationParameterType.Bool;
				return true;
			default:
				type = LocalizationParameterType.String;
				return false;
		}
	}

	/// <summary>
	/// The C# type a generated parameter of this type uses. A text parameter is <c>LocalizedText</c>, not
	/// <c>string</c>: a caption substituted into another string is itself often localized - "{field} is
	/// required" - and a <c>string</c> parameter would force the caller to resolve it early, producing a
	/// half-translated sentence. <c>string</c> converts implicitly, so a literal call site is unaffected.
	/// </summary>
	public static string ToCSharpKeyword(LocalizationParameterType type)
	{
		switch (type)
		{
			case LocalizationParameterType.Int: return "int";
			case LocalizationParameterType.Long: return "long";
			case LocalizationParameterType.Double: return "double";
			case LocalizationParameterType.Bool: return "bool";
			default: return "global::MacroDeck.Localization.LocalizedText";
		}
	}

	/// <summary>The TypeScript type a generated parameter of this type uses.</summary>
	public static string ToTypeScriptType(LocalizationParameterType type)
	{
		switch (type)
		{
			case LocalizationParameterType.Int:
			case LocalizationParameterType.Long:
			case LocalizationParameterType.Double:
				return "number";
			case LocalizationParameterType.Bool:
				return "boolean";
			default:
				return "string";
		}
	}

	/// <summary>Every name accepted by <see cref="TryParse" />, for diagnostic messages.</summary>
	public static string KnownNames => "string, int, long, double, bool";
}
