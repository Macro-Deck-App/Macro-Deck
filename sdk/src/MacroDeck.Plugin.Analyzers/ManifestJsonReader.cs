using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Reads the handful of top-level string properties the manifest rules need from a plugin's
/// <c>manifest.json</c>: <c>id</c>, <c>name</c> and <c>version</c> for <c>MDP1001</c>, <c>icon</c> for
/// <c>MDP1003</c>.
///
/// <para>
/// Deliberately not a general JSON parser, and deliberately not a reference to a JSON library: an
/// analyzer loads into the compiler host process itself, where pulling in a dependency solely to read
/// three flat string fields is heavier - and more likely to collide with whatever version the consuming
/// project's own compilation already carries - than this narrow a job justifies. See
/// <c>ListenerUrlOverrideAnalyzer</c>'s own <c>launchSettings.json</c> check for the same reasoning
/// applied to an even smaller job (a substring search).
/// </para>
///
/// <para>
/// This is a minimal tokenizer: it tracks string/escape state and object/array nesting depth well enough
/// to find a top-level (depth-1) key's string value and report where in the file that value's own quoted
/// literal starts and ends. It does not validate that the rest of the document is well-formed JSON, does
/// not resolve duplicate keys any particular way (last one found wins), and silently finds nothing for a
/// document that is not a single top-level object - all of which is fine, because a document this reader
/// cannot make sense of is also one <c>PluginHostBuilder.Build</c> already rejects at runtime, which is
/// the backstop this analyzer is layered on top of, not a replacement for.
/// </para>
/// </summary>
internal static class ManifestJsonReader
{
	/// <summary>One top-level string property this reader found.</summary>
	public readonly struct ManifestStringProperty
	{
		public ManifestStringProperty(string value, TextSpan valueSpan)
		{
			Value = value;
			ValueSpan = valueSpan;
		}

		/// <summary>The decoded string value, with any <c>\" \\ \n</c> and similar escapes resolved.</summary>
		public string Value { get; }

		/// <summary>The span of the value's own quoted literal (including both quote characters) in the source text.</summary>
		public TextSpan ValueSpan { get; }
	}

	/// <summary>
	/// Finds every requested top-level key's string value in <paramref name="json" />. A key that is
	/// present but not a JSON string (a number, an object, an array) is left out of the result exactly
	/// like a key that is absent altogether - MDP1001 treats "not found" and "not a string" the same way,
	/// since neither is a value this rule can validate as an id, a name or a version.
	/// </summary>
	public static IReadOnlyDictionary<string, ManifestStringProperty> ReadTopLevelStrings(
		string json,
		IReadOnlyCollection<string> keys)
	{
		var result = new Dictionary<string, ManifestStringProperty>(StringComparer.Ordinal);
		var length = json.Length;
		var index = 0;
		var depth = 0;

		while (index < length)
		{
			var current = json[index];

			if (current == '"')
			{
				var stringStart = index;
				var stringEnd = SkipString(json, index);

				if (stringEnd < 0)
				{
					// Unterminated string literal - the document is malformed in a way this reader
					// cannot safely continue past.
					break;
				}

				index = stringEnd;

				// A JSON string is a *value* unless immediately followed (modulo whitespace) by ':' -
				// only then is it a key, and only a depth-1 key is one of the top-level properties this
				// reader looks for.
				if (depth == 1)
				{
					var afterKey = SkipWhitespace(json, index);

					if (afterKey < length && json[afterKey] == ':')
					{
						var key = Decode(json, stringStart, stringEnd);
						var valueStart = SkipWhitespace(json, afterKey + 1);

						if (valueStart < length && json[valueStart] == '"')
						{
							var valueEnd = SkipString(json, valueStart);

							if (valueEnd >= 0)
							{
								if (keys.Contains(key))
								{
									result[key] = new ManifestStringProperty(Decode(json, valueStart, valueEnd),
										TextSpan.FromBounds(valueStart, valueEnd));
								}

								index = valueEnd;
							}
						}
					}
				}

				continue;
			}

			switch (current)
			{
				case '{' or '[':
					depth++;
					break;
				case '}' or ']':
					depth--;
					break;
			}

			index++;
		}

		return result;
	}

	/// <summary><paramref name="start" /> must be the index of the opening quote. Returns the index one
	/// past the closing quote, or -1 when the string never closes.</summary>
	private static int SkipString(string json, int start)
	{
		var index = start + 1;

		while (index < json.Length)
		{
			var character = json[index];

			if (character == '\\')
			{
				// Skips whatever follows the backslash without interpreting it - sufficient to not
				// mistake an escaped quote (\") for the string's own closing quote, which is all this
				// scan needs from escape handling.
				index += 2;
				continue;
			}

			if (character == '"')
			{
				return index + 1;
			}

			index++;
		}

		return -1;
	}

	private static int SkipWhitespace(string json, int index)
	{
		while (index < json.Length && char.IsWhiteSpace(json[index]))
		{
			index++;
		}

		return index;
	}

	/// <summary>Decodes the standard JSON escapes inside the quoted literal <c>json[start..end)</c>,
	/// where <paramref name="start" /> is the opening quote and <paramref name="end" /> is one past the
	/// closing quote.</summary>
	private static string Decode(string json, int start, int end)
	{
		var inner = json.Substring(start + 1, end - start - 2);

		if (inner.IndexOf('\\') < 0)
		{
			return inner;
		}

		var builder = new StringBuilder(inner.Length);

		for (var i = 0; i < inner.Length; i++)
		{
			var character = inner[i];

			if (character != '\\' || i == inner.Length - 1)
			{
				builder.Append(character);
				continue;
			}

			i++;
			var escaped = inner[i];

			if (escaped == 'u' && i + 4 < inner.Length)
			{
				var hex = inner.Substring(i + 1, 4);
				i += 4;
				builder.Append((char)Convert.ToInt32(hex, 16));
				continue;
			}

			builder.Append(escaped switch
			{
				'"' => '"',
				'\\' => '\\',
				'/' => '/',
				'b' => '\b',
				'f' => '\f',
				'n' => '\n',
				'r' => '\r',
				't' => '\t',
				_ => escaped
			});
		}

		return builder.ToString();
	}
}
