namespace MacroDeckHost.Rendering.Dom;

internal sealed record CssAttributeTest(string Name, string? Value);

internal sealed record CssCompound(
	string? Tag,
	IReadOnlyList<string> Classes,
	IReadOnlyList<CssAttributeTest> Attributes,
	IReadOnlyList<CssCompound> Not,
	bool NeverMatches);

internal sealed class CssSelector
{
	private CssSelector(IReadOnlyList<CssCompound> compounds, IReadOnlyList<char> combinators, int specificity)
	{
		Compounds = compounds;
		Combinators = combinators;
		Specificity = specificity;
	}

	public IReadOnlyList<CssCompound> Compounds { get; }

	public IReadOnlyList<char> Combinators { get; }

	public int Specificity { get; }

	public static CssSelector? Parse(string text)
	{
		if (text.Contains("::", StringComparison.Ordinal))
		{
			return null;
		}

		var compounds = new List<CssCompound>();
		var combinators = new List<char>();
		var index = 0;
		var specificity = 0;
		var pending = ' ';
		while (index < text.Length)
		{
			if (char.IsWhiteSpace(text[index]))
			{
				index++;
				continue;
			}

			if (text[index] is '>' or '+' or '~')
			{
				pending = text[index];
				index++;
				continue;
			}

			var compound = ParseCompound(text, ref index, ref specificity);
			if (compounds.Count > 0)
			{
				combinators.Add(pending);
			}

			compounds.Add(compound);
			pending = ' ';
		}

		if (compounds.Count == 0)
		{
			return null;
		}

		compounds.Reverse();
		combinators.Reverse();
		return new CssSelector(compounds, combinators, specificity);
	}

	public bool Matches(Element element)
	{
		return MatchFrom(element, 0);
	}

	private bool MatchFrom(Element element, int at)
	{
		if (!MatchesCompound(Compounds[at], element))
		{
			return false;
		}

		if (at == Compounds.Count - 1)
		{
			return true;
		}

		var combinator = Combinators[at];
		if (combinator == '>')
		{
			return element.Parent is { } parent && MatchFrom(parent, at + 1);
		}

		if (combinator == ' ')
		{
			for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
			{
				if (MatchFrom(ancestor, at + 1))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool MatchesCompound(CssCompound compound, Element element)
	{
		if (compound.NeverMatches || element.IsText)
		{
			return false;
		}

		if (compound.Tag is { } tag
			&& tag != "*"
			&& !string.Equals(tag, element.Tag, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		foreach (var name in compound.Classes)
		{
			if (!element.Classes.Contains(name))
			{
				return false;
			}
		}

		foreach (var attribute in compound.Attributes)
		{
			if (!element.Attributes.TryGetValue(attribute.Name, out var value)
				|| attribute.Value is not null && attribute.Value != value)
			{
				return false;
			}
		}

		foreach (var negated in compound.Not)
		{
			if (MatchesCompound(negated, element))
			{
				return false;
			}
		}

		return true;
	}

	private static CssCompound ParseCompound(string text, ref int index, ref int specificity)
	{
		string? tag = null;
		var classes = new List<string>();
		var attributes = new List<CssAttributeTest>();
		var negations = new List<CssCompound>();
		var never = false;
		while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('>' or '+' or '~'))
		{
			var current = text[index];
			if (current == '.')
			{
				index++;
				classes.Add(ReadName(text, ref index));
				specificity += 100;
			}
			else if (current == '#')
			{
				index++;
				ReadName(text, ref index);
				never = true;
				specificity += 10000;
			}
			else if (current == '[')
			{
				var close = text.IndexOf(']', index);
				var inner = text[(index + 1)..close];
				index = close + 1;
				specificity += 100;
				var equals = inner.IndexOf('=', StringComparison.Ordinal);
				if (equals < 0)
				{
					attributes.Add(new CssAttributeTest(inner.Trim(), null));
				}
				else if (equals > 0 && inner[equals - 1] is '|' or '~' or '^' or '$' or '*')
				{
					never = true;
				}
				else
				{
					attributes.Add(new CssAttributeTest(inner[..equals].Trim(),
						inner[(equals + 1)..].Trim().Trim('"', '\'')));
				}
			}
			else if (current == ':')
			{
				index++;
				var name = ReadName(text, ref index);
				if (name == "not" && index < text.Length && text[index] == '(')
				{
					var close = text.IndexOf(')', index);
					var inner = text[(index + 1)..close];
					index = close + 1;
					var innerIndex = 0;
					var ignored = 0;
					negations.Add(ParseCompound(inner, ref innerIndex, ref ignored));
					specificity += 100;
				}
				else
				{
					if (index < text.Length && text[index] == '(')
					{
						index = text.IndexOf(')', index) + 1;
					}

					never = true;
				}
			}
			else if (current == '*')
			{
				index++;
				tag = "*";
			}
			else
			{
				tag = ReadName(text, ref index);
				specificity += 1;
			}
		}

		return new CssCompound(tag, classes, attributes, negations, never);
	}

	private static string ReadName(string text, ref int index)
	{
		var start = index;
		while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '-' or '_'))
		{
			index++;
		}

		return text[start..index];
	}
}
