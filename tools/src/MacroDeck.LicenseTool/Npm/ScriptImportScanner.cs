using System.Text;

namespace MacroDeck.LicenseTool.Npm;

internal static class ScriptImportScanner
{
	private enum TokenKind
	{
		Identifier,
		String,
		Punctuator,
		Other,
	}

	private readonly record struct Token(TokenKind Kind, string Text);

	private static readonly HashSet<string> RegexPrecedingKeywords = new(StringComparer.Ordinal)
	{
		"return", "typeof", "case", "do", "else", "in", "of", "new", "delete", "void", "throw", "instanceof", "yield",
		"await",
	};

	public static IReadOnlyList<string> Scan(string source)
	{
		var tokens = Tokenize(source);
		var specifiers = new List<string>();
		for (var index = 0; index < tokens.Count; index++)
		{
			var token = tokens[index];
			if (token.Kind != TokenKind.Identifier || (index > 0 && tokens[index - 1].Text == "."))
			{
				continue;
			}

			var specifier = token.Text switch
			{
				"import" => ReadImport(tokens, index),
				"export" => ReadExport(tokens, index),
				"require" => ReadCall(tokens, index),
				_ => null,
			};
			if (specifier is not null)
			{
				specifiers.Add(specifier);
			}
		}

		return specifiers;
	}

	private static string? ReadImport(List<Token> tokens, int index)
	{
		var next = At(tokens, index + 1);
		if (next is null || next.Value.Text == ".")
		{
			return null;
		}

		if (next.Value.Text == "(")
		{
			return ReadCall(tokens, index);
		}

		if (next.Value.Kind == TokenKind.String)
		{
			return next.Value.Text;
		}

		if (next.Value.Text == "type" && At(tokens, index + 2) is { } afterType
			&& afterType.Text is not ("from" or ",") && afterType.Kind != TokenKind.String)
		{
			return null;
		}

		return ReadFrom(tokens, index + 1);
	}

	private static string? ReadExport(List<Token> tokens, int index)
	{
		var next = At(tokens, index + 1);
		if (next is null)
		{
			return null;
		}

		if (next.Value.Text == "*")
		{
			return ReadFrom(tokens, index + 2);
		}

		if (next.Value.Text != "{")
		{
			return null;
		}

		var depth = 0;
		for (var position = index + 1; position < tokens.Count; position++)
		{
			if (tokens[position].Text == "{")
			{
				depth++;
			}
			else if (tokens[position].Text == "}" && --depth == 0)
			{
				return At(tokens, position + 1) is { Text: "from" } && At(tokens, position + 2) is { Kind: TokenKind.String } target
					? target.Text
					: null;
			}
		}

		return null;
	}

	private static string? ReadFrom(List<Token> tokens, int start)
	{
		for (var position = start; position < tokens.Count && position < start + 400; position++)
		{
			var token = tokens[position];
			if (token.Text is ";" || (token.Kind == TokenKind.Identifier && token.Text is "import" or "export"))
			{
				return null;
			}

			if (token.Kind == TokenKind.Identifier && token.Text == "from"
				&& At(tokens, position + 1) is { Kind: TokenKind.String } target)
			{
				return target.Text;
			}
		}

		return null;
	}

	private static string? ReadCall(List<Token> tokens, int index) =>
		At(tokens, index + 1) is { Text: "(" }
		&& At(tokens, index + 2) is { Kind: TokenKind.String } argument
		&& At(tokens, index + 3) is { Text: ")" or "," }
			? argument.Text
			: null;

	private static Token? At(List<Token> tokens, int index) => index < tokens.Count ? tokens[index] : null;

	private static List<Token> Tokenize(string source)
	{
		var tokens = new List<Token>();
		var index = 0;
		Lex(source, ref index, tokens, stopAtBrace: false);
		return tokens;
	}

	private static void Lex(string source, ref int index, List<Token> tokens, bool stopAtBrace)
	{
		var braceDepth = 0;
		while (index < source.Length)
		{
			var character = source[index];
			if (char.IsWhiteSpace(character))
			{
				index++;
			}
			else if (character == '/' && Peek(source, index + 1) == '/')
			{
				while (index < source.Length && source[index] != '\n')
				{
					index++;
				}
			}
			else if (character == '/' && Peek(source, index + 1) == '*')
			{
				var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
				index = end < 0 ? source.Length : end + 2;
			}
			else if (character is '\'' or '"')
			{
				tokens.Add(new Token(TokenKind.String, ReadString(source, ref index, character)));
			}
			else if (character == '`')
			{
				SkipTemplate(source, ref index);
				tokens.Add(new Token(TokenKind.Other, "`"));
			}
			else if (character == '/' && StartsRegex(tokens))
			{
				SkipRegex(source, ref index);
				tokens.Add(new Token(TokenKind.Other, "/regex/"));
			}
			else if (char.IsLetter(character) || character is '_' or '$')
			{
				var start = index;
				while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$'))
				{
					index++;
				}

				tokens.Add(new Token(TokenKind.Identifier, source[start..index]));
			}
			else if (char.IsDigit(character))
			{
				while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '.' or '_'))
				{
					index++;
				}

				tokens.Add(new Token(TokenKind.Other, "0"));
			}
			else
			{
				if (character == '{')
				{
					braceDepth++;
				}
				else if (character == '}')
				{
					if (stopAtBrace && braceDepth == 0)
					{
						index++;
						return;
					}

					braceDepth--;
				}

				tokens.Add(new Token(TokenKind.Punctuator, character.ToString()));
				index++;
			}
		}
	}

	private static bool StartsRegex(List<Token> tokens)
	{
		if (tokens.Count == 0)
		{
			return true;
		}

		var previous = tokens[^1];
		return previous.Kind switch
		{
			TokenKind.Identifier => RegexPrecedingKeywords.Contains(previous.Text),
			TokenKind.String or TokenKind.Other => false,
			_ => previous.Text is not (")" or "]"),
		};
	}

	private static string ReadString(string source, ref int index, char quote)
	{
		var builder = new StringBuilder();
		index++;
		while (index < source.Length && source[index] != quote && source[index] != '\n')
		{
			if (source[index] == '\\' && index + 1 < source.Length)
			{
				index++;
			}

			builder.Append(source[index]);
			index++;
		}

		index++;
		return builder.ToString();
	}

	private static void SkipTemplate(string source, ref int index)
	{
		index++;
		while (index < source.Length && source[index] != '`')
		{
			if (source[index] == '\\')
			{
				index += 2;
			}
			else if (source[index] == '$' && Peek(source, index + 1) == '{')
			{
				index += 2;
				Lex(source, ref index, [], stopAtBrace: true);
			}
			else
			{
				index++;
			}
		}

		index++;
	}

	private static void SkipRegex(string source, ref int index)
	{
		index++;
		var inClass = false;
		while (index < source.Length && source[index] != '\n')
		{
			var character = source[index];
			if (character == '\\')
			{
				index += 2;
				continue;
			}

			if (character == '[')
			{
				inClass = true;
			}
			else if (character == ']')
			{
				inClass = false;
			}
			else if (character == '/' && !inClass)
			{
				index++;
				break;
			}

			index++;
		}

		while (index < source.Length && char.IsLetter(source[index]))
		{
			index++;
		}
	}

	private static char Peek(string source, int index) => index < source.Length ? source[index] : '\0';
}
