namespace MacroDeck.LicenseTool.Licensing;

internal abstract record SpdxExpression
{
	public static SpdxExpression Parse(string text)
	{
		var parser = new Parser(Tokenize(text), text);
		var expression = parser.ParseOr();
		parser.ExpectEnd();
		return expression;
	}

	private static List<string> Tokenize(string text)
	{
		var tokens = new List<string>();
		var index = 0;
		while (index < text.Length)
		{
			var character = text[index];
			if (char.IsWhiteSpace(character))
			{
				index++;
				continue;
			}

			if (character is '(' or ')' or '/')
			{
				tokens.Add(character.ToString());
				index++;
				continue;
			}

			var start = index;
			while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('(' or ')' or '/'))
			{
				index++;
			}

			tokens.Add(text[start..index]);
		}

		return tokens;
	}

	private sealed class Parser(List<string> tokens, string source)
	{
		private int _position;

		public SpdxExpression ParseOr()
		{
			var operands = new List<SpdxExpression> { ParseAnd() };
			while (Peek() is { } token && (IsOperator(token, "OR") || token == "/"))
			{
				_position++;
				operands.Add(ParseAnd());
			}

			return operands.Count == 1 ? operands[0] : new SpdxOr(operands);
		}

		public void ExpectEnd()
		{
			if (_position != tokens.Count)
			{
				throw Invalid();
			}
		}

		private SpdxExpression ParseAnd()
		{
			var operands = new List<SpdxExpression> { ParseWith() };
			while (Peek() is { } token && IsOperator(token, "AND"))
			{
				_position++;
				operands.Add(ParseWith());
			}

			return operands.Count == 1 ? operands[0] : new SpdxAnd(operands);
		}

		private SpdxExpression ParseWith()
		{
			var primary = ParsePrimary();
			if (Peek() is { } token && IsOperator(token, "WITH"))
			{
				_position++;
				var exception = Next();
				if (primary is not SpdxLicense license || IsKeyword(exception))
				{
					throw Invalid();
				}

				return license with { Exception = exception };
			}

			return primary;
		}

		private SpdxExpression ParsePrimary()
		{
			var token = Next();
			if (token == "(")
			{
				var inner = ParseOr();
				if (Next() != ")")
				{
					throw Invalid();
				}

				return inner;
			}

			if (token is ")" or "/" || IsKeyword(token))
			{
				throw Invalid();
			}

			return new SpdxLicense(token.TrimEnd('+'), null);
		}

		private string? Peek() => _position < tokens.Count ? tokens[_position] : null;

		private string Next() => _position < tokens.Count ? tokens[_position++] : throw Invalid();

		private static bool IsOperator(string token, string name) =>
			string.Equals(token, name, StringComparison.OrdinalIgnoreCase);

		private static bool IsKeyword(string token) =>
			IsOperator(token, "AND") || IsOperator(token, "OR") || IsOperator(token, "WITH");

		private FormatException Invalid() => new($"invalid SPDX license expression '{source}'");
	}
}

internal sealed record SpdxLicense(string Id, string? Exception) : SpdxExpression;

internal sealed record SpdxAnd(IReadOnlyList<SpdxExpression> Operands) : SpdxExpression;

internal sealed record SpdxOr(IReadOnlyList<SpdxExpression> Operands) : SpdxExpression;
