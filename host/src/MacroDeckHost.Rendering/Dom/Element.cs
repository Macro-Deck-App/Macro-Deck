using MacroDeckHost.Rendering.Layout;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Dom;

internal sealed class Element
{
	public const string TextTag = "#text";

	public Element(string tag, string? text = null)
	{
		Tag = tag;
		Text = text;
	}

	public string Tag { get; }

	public string? Text { get; set; }

	public Element? Parent { get; private set; }

	public List<Element> Children { get; } = [];

	public HashSet<string> Classes { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> Style { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

	public UiNode? Node { get; set; }

	public object? Paint { get; set; }

	public ComputedStyle Computed { get; set; } = ComputedStyle.Empty();

	public BoxFragment Box { get; } = new();

	public bool IsText => Tag == TextTag;

	public string? NodeId => Attributes.GetValueOrDefault("data-node-id");

	public void Append(Element child)
	{
		child.Parent?.Children.Remove(child);
		child.Parent = this;
		Children.Add(child);
	}

	public void SetClassName(string value)
	{
		Classes.Clear();
		foreach (var name in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			Classes.Add(name);
		}
	}

	public void SetClass(string name, bool on)
	{
		if (on)
		{
			Classes.Add(name);
		}
		else
		{
			Classes.Remove(name);
		}
	}

	public void SetStyle(string name, string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			Style.Remove(name);
		}
		else
		{
			Style[name] = value;
		}
	}

	public void SetAttribute(string name, string? value)
	{
		if (value is null)
		{
			Attributes.Remove(name);
		}
		else
		{
			Attributes[name] = value;
		}
	}

	public string TextContent()
	{
		if (IsText)
		{
			return Text ?? "";
		}

		var builder = new System.Text.StringBuilder();
		AppendText(this, builder);
		return builder.ToString();
	}

	public IEnumerable<Element> Descendants()
	{
		foreach (var child in Children)
		{
			yield return child;
			foreach (var nested in child.Descendants())
			{
				yield return nested;
			}
		}
	}

	public Element? Closest(Func<Element, bool> predicate)
	{
		for (var current = this; current is not null; current = current.Parent)
		{
			if (predicate(current))
			{
				return current;
			}
		}

		return null;
	}

	private static void AppendText(Element element, System.Text.StringBuilder builder)
	{
		foreach (var child in element.Children)
		{
			if (child.IsText)
			{
				builder.Append(child.Text);
			}
			else
			{
				AppendText(child, builder);
			}
		}
	}
}

internal sealed class BoxFragment
{
	public LayoutUnit X { get; set; }

	public LayoutUnit Y { get; set; }

	public LayoutUnit Width { get; set; }

	public LayoutUnit Height { get; set; }

	public double ScrollTop { get; set; }

	public double ScrollLeft { get; set; }

	public LayoutUnit ScrollableWidth { get; set; }

	public LayoutUnit ScrollableHeight { get; set; }

	public LayoutUnit ScrollbarRight { get; set; }

	public LayoutUnit ScrollbarBottom { get; set; }

	public List<TextLine> Lines { get; } = [];

	public LayoutUnit Baseline { get; set; }

	public bool HasBaseline { get; set; }

	public bool Displayed { get; set; }
}
