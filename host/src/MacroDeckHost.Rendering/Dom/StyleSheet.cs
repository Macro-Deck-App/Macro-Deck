using System.Reflection;
using System.Text;

namespace MacroDeckHost.Rendering.Dom;

internal sealed record CssDeclaration(string Name, string Value, bool Important);

internal sealed record CssRule(CssSelector Selector, int Order, IReadOnlyList<CssDeclaration> Declarations);

internal sealed class StyleSheet
{
	private static readonly Lazy<StyleSheet> _shared = new(Load);

	private StyleSheet(
		IReadOnlyList<CssRule> rules,
		IReadOnlyDictionary<string, string> darkTokens,
		IReadOnlyDictionary<string, string> lightTokens)
	{
		Rules = rules;
		DarkTokens = darkTokens;
		LightTokens = lightTokens;
	}

	public static StyleSheet Shared => _shared.Value;

	public IReadOnlyList<CssRule> Rules { get; }

	public IReadOnlyDictionary<string, string> DarkTokens { get; }

	public IReadOnlyDictionary<string, string> LightTokens { get; }

	public static StyleSheet Parse(IEnumerable<string> sheets)
	{
		var rules = new List<CssRule>();
		var dark = new Dictionary<string, string>(StringComparer.Ordinal);
		var light = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var sheet in sheets)
		{
			ParseBlock(StripComments(sheet), rules, dark, light, false);
		}

		return new StyleSheet(rules, dark, light);
	}

	private static readonly string[] _sheets = ["tokens.css", "reset.css", "renderer.css", "widget-border.css"];

	private static StyleSheet Load()
	{
		var assembly = typeof(StyleSheet).Assembly;
		return Parse(_sheets.Select(name => ReadResource(assembly, $"MacroDeckHost.Rendering.Styles.{name}")));
	}

	private static string ReadResource(Assembly assembly, string name)
	{
		using var stream = assembly.GetManifestResourceStream(name)
			?? throw new InvalidOperationException($"missing stylesheet {name}");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private static string StripComments(string css)
	{
		var builder = new StringBuilder(css.Length);
		for (var index = 0; index < css.Length; index++)
		{
			if (css[index] == '/' && index + 1 < css.Length && css[index + 1] == '*')
			{
				var end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
				index = end < 0 ? css.Length : end + 1;
				continue;
			}

			builder.Append(css[index]);
		}

		return builder.ToString();
	}

	private static void ParseBlock(
		string css,
		List<CssRule> rules,
		Dictionary<string, string> dark,
		Dictionary<string, string> light,
		bool skip)
	{
		var index = 0;
		while (index < css.Length)
		{
			var open = css.IndexOf('{', index);
			if (open < 0)
			{
				return;
			}

			var prelude = css[index..open].Trim();
			var close = MatchingBrace(css, open);
			var body = css[(open + 1)..close];
			index = close + 1;
			if (prelude.StartsWith('@'))
			{
				// Motion preferences and keyframes only carry transitions; @supports guards the ring.
				if (prelude.StartsWith("@supports", StringComparison.Ordinal))
				{
					ParseBlock(body, rules, dark, light, skip);
				}

				continue;
			}

			if (skip)
			{
				continue;
			}

			var declarations = ParseDeclarations(body);
			if (prelude == ":root" || prelude == ".light")
			{
				var target = prelude == ":root" ? dark : light;
				foreach (var declaration in
					declarations.Where(item => item.Name.StartsWith("--", StringComparison.Ordinal)))
				{
					target[declaration.Name] = declaration.Value;
				}

				continue;
			}

			foreach (var selectorText in SplitTopLevel(prelude, ','))
			{
				var selector = CssSelector.Parse(selectorText.Trim());
				if (selector is not null)
				{
					rules.Add(new CssRule(selector, rules.Count, declarations));
				}
			}
		}
	}

	private static int MatchingBrace(string css, int open)
	{
		var depth = 0;
		for (var index = open; index < css.Length; index++)
		{
			if (css[index] == '{')
			{
				depth++;
			}
			else if (css[index] == '}')
			{
				depth--;
				if (depth == 0)
				{
					return index;
				}
			}
		}

		return css.Length - 1;
	}

	private static List<CssDeclaration> ParseDeclarations(string body)
	{
		var declarations = new List<CssDeclaration>();
		foreach (var part in SplitTopLevel(body, ';'))
		{
			var colon = part.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			var name = part[..colon].Trim();
			var value = part[(colon + 1)..].Trim();
			var important = value.EndsWith("!important", StringComparison.Ordinal);
			if (important)
			{
				value = value[..^"!important".Length].Trim();
			}

			declarations.Add(new CssDeclaration(name, value, important));
		}

		return declarations;
	}

	internal static List<string> SplitTopLevel(string text, char separator)
	{
		var parts = new List<string>();
		var depth = 0;
		var start = 0;
		for (var index = 0; index < text.Length; index++)
		{
			switch (text[index])
			{
				case '(':
				case '[':
					depth++;
					break;
				case ')':
				case ']':
					depth--;
					break;
				default:
					if (text[index] == separator && depth == 0)
					{
						parts.Add(text[start..index]);
						start = index + 1;
					}

					break;
			}
		}

		if (start < text.Length)
		{
			parts.Add(text[start..]);
		}

		return parts;
	}
}
