namespace MacroDeck.Plugin.Analyzers;

/// <summary>The C# reserved words a generated member name has to be escaped past.</summary>
/// <remarks>A resource author picking <c>Common.New</c> should get a member called <c>New</c>, not a
/// renamed one, so the emitter writes <c>@new</c>-style escapes rather than mangling the name.</remarks>
internal static class LocalizationKeywords
{
	private static readonly HashSet<string> _keywords = new(StringComparer.Ordinal)
	{
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
		"const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
		"explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
		"implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
		"null", "object", "operator", "out", "override", "params", "private", "protected", "public",
		"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
		"string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
		"unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
	};

	/// <summary>Whether <paramref name="identifier" /> is a C# reserved word.</summary>
	public static bool IsKeyword(string identifier) => _keywords.Contains(identifier);
}
