using System.Text;

namespace MacroDeckHost.Integrations.Steam.Native;

internal sealed class ValveKeyValues
{
	private const int MaximumDepth = 24;

	private static readonly IReadOnlyDictionary<string, ValveKeyValues> _noChildren =
		new Dictionary<string, ValveKeyValues>(StringComparer.OrdinalIgnoreCase);

	private ValveKeyValues(string? value, IReadOnlyDictionary<string, ValveKeyValues> children)
	{
		Value = value;
		Children = children;
	}

	public string? Value { get; }

	public IReadOnlyDictionary<string, ValveKeyValues> Children { get; }

	public ValveKeyValues? this[string key] => Children.GetValueOrDefault(key);

	public ValveKeyValues? Find(params string[] path)
	{
		var node = this;
		foreach (var key in path)
		{
			node = node[key];
			if (node is null)
			{
				return null;
			}
		}

		return node;
	}

	public static ValveKeyValues? TryParse(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}

		var reader = new Reader(text.TrimStart('﻿'));
		return reader.ReadBlock(0, true) is { } children
			? new ValveKeyValues(null, children)
			: null;
	}

	private sealed class Reader(string text)
	{
		private int _position;

		public Dictionary<string, ValveKeyValues>? ReadBlock(int depth, bool topLevel)
		{
			if (depth > MaximumDepth)
			{
				return null;
			}

			var children = new Dictionary<string, ValveKeyValues>(StringComparer.OrdinalIgnoreCase);

			while (true)
			{
				var token = Next();
				if (token.Kind == TokenKind.End)
				{
					return topLevel ? children : null;
				}

				if (token.Kind == TokenKind.Close)
				{
					return topLevel ? null : children;
				}

				if (token.Kind != TokenKind.Text)
				{
					return null;
				}

				var next = Next();
				switch (next.Kind)
				{
					case TokenKind.Text:
						children[token.Text!] = new ValveKeyValues(next.Text, _noChildren);
						break;
					case TokenKind.Open:
						if (ReadBlock(depth + 1, false) is not { } nested)
						{
							return null;
						}

						children[token.Text!] = new ValveKeyValues(null, nested);
						break;
					default:
						return null;
				}
			}
		}

		private Token Next()
		{
			SkipTrivia();

			if (_position >= text.Length)
			{
				return new Token(TokenKind.End, null);
			}

			switch (text[_position])
			{
				case '{':
					_position++;
					return new Token(TokenKind.Open, null);
				case '}':
					_position++;
					return new Token(TokenKind.Close, null);
				case '"':
					return ReadQuoted();
				default:
					return ReadBare();
			}
		}

		private void SkipTrivia()
		{
			while (_position < text.Length)
			{
				var current = text[_position];
				if (char.IsWhiteSpace(current))
				{
					_position++;
				}
				else if (current == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
				{
					while (_position < text.Length && text[_position] != '\n')
					{
						_position++;
					}
				}
				else
				{
					return;
				}
			}
		}

		private Token ReadQuoted()
		{
			_position++;
			var builder = new StringBuilder();

			while (_position < text.Length)
			{
				var current = text[_position++];
				if (current == '"')
				{
					return new Token(TokenKind.Text, builder.ToString());
				}

				if (current == '\\' && _position < text.Length)
				{
					var escaped = text[_position++];
					builder.Append(escaped switch
					{
						'n' => '\n',
						't' => '\t',
						_ => escaped
					});
				}
				else
				{
					builder.Append(current);
				}
			}

			return new Token(TokenKind.Invalid, null);
		}

		private Token ReadBare()
		{
			var start = _position;
			while (_position < text.Length
				&& !char.IsWhiteSpace(text[_position])
				&& text[_position] is not ('{' or '}' or '"'))
			{
				_position++;
			}

			return new Token(TokenKind.Text, text[start.._position]);
		}
	}

	private readonly record struct Token(TokenKind Kind, string? Text);

	private enum TokenKind
	{
		Text,

		Open,

		Close,

		End,

		Invalid
	}
}
