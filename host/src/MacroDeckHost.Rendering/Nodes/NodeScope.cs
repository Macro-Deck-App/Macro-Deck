using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal readonly record struct BoxSize(double? Width, double? Height)
{
	public double? Extent => UiLength.BoxExtent(Width, Height);
}

internal sealed record ChildEntry(
	UiNode Child,
	BoxSize Box,
	double? CrossExtent,
	bool HasContainer = false,
	double? Container = null);

internal sealed class NodeScope
{
	private readonly Dictionary<string, Element> _parts = new(StringComparer.Ordinal);

	public required TreeBuilder Builder { get; init; }

	public required RenderHost Host { get; init; }

	public required Element Element { get; init; }

	public required UiNode Node { get; init; }

	public required double Basis { get; init; }

	public double? Container { get; init; }

	public double? CrossExtent { get; init; }

	public required BoxSize Box { get; init; }

	public BoxSize? GivenBox { get; init; }

	public bool IsTreeRoot { get; init; }

	public bool ParentDisabled { get; init; }

	public bool ParentInsideButton { get; init; }

	public bool TransparentRoot { get; init; }

	public LengthScope Scope => new(Basis, CrossExtent, Container);

	public bool IsDisabled => ParentDisabled || Modifiers.IsDisabledRegion(Node);

	public bool InsideButton => ParentInsideButton || Node.Type == Components.Button;

	public static string Px(double? value)
	{
		return value is { } number ? string.Create(CultureInfo.InvariantCulture, $"{number}px") : "";
	}

	public static string Num(double value)
	{
		return RenderHost.FormatNumber(value);
	}

	public double? Resolve(string key)
	{
		return UiLength.Resolve(UiLength.Of(Node, key), Scope);
	}

	public Element Part(string name, string tag, Element? parent = null)
	{
		if (!_parts.TryGetValue(name, out var element))
		{
			element = new Element(tag);
			_parts[name] = element;
		}

		var target = parent ?? Element;
		if (element.Parent != target)
		{
			target.Append(element);
		}

		return element;
	}

	public static void SizeTo(Element element, BoxSize size)
	{
		element.SetStyle("width", Px(size.Width));
		element.SetStyle("height", Px(size.Height));
	}

	public void SyncChildren(Element parent, IEnumerable<ChildEntry> entries)
	{
		foreach (var entry in entries)
		{
			var container = entry.HasContainer ? entry.Container : entry.Box.Extent;
			Builder.Render(parent,
				entry.Child,
				entry.Box,
				entry.CrossExtent,
				Basis,
				container,
				IsDisabled,
				InsideButton,
				TransparentRoot ? IsTreeRoot : null);
		}
	}

	public void PressTint()
	{
		var claims = Modifiers.ClaimsGesture(Node) && !IsDisabled;
		Element.SetClass("widget-pressable", claims);
		if (claims)
		{
			Part("tint", "div").SetClassName("widget-press-tint");
		}
	}

	public void KeepFit(Element element, double? declared, double? minimum, int maxLines)
	{
		Host.Fits.Add(new TextFitEntry(element, declared, minimum, maxLines));
	}
}
