using System.Globalization;
using MacroDeckHost.Rendering.Dom;

namespace MacroDeckHost.Rendering.Layout;

internal readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
	public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

	public static Affine Translate(double x, double y)
	{
		return new Affine(1, 0, 0, 1, x, y);
	}

	public static Affine Scale(double x, double y)
	{
		return new Affine(x, 0, 0, y, 0, 0);
	}

	public static Affine Rotate(double degrees)
	{
		var radians = degrees * Math.PI / 180;
		var cos = Math.Cos(radians);
		var sin = Math.Sin(radians);
		return new Affine(cos, sin, -sin, cos, 0, 0);
	}

	// this then other: a point is mapped by other first.
	public Affine Then(Affine other)
	{
		return new Affine(A * other.A + C * other.B,
			B * other.A + D * other.B,
			A * other.C + C * other.D,
			B * other.C + D * other.D,
			A * other.E + C * other.F + E,
			B * other.E + D * other.F + F);
	}

	public (double X, double Y) Map(double x, double y)
	{
		return (A * x + C * y + E, B * x + D * y + F);
	}

	public bool IsIdentity => this == Identity;
}

internal readonly record struct RectD(double X, double Y, double Width, double Height);

internal static class Geometry
{
	public static Affine OwnTransform(Element element)
	{
		var value = element.Computed.Get("transform", "none");
		if (value is "none" or "")
		{
			return Affine.Identity;
		}

		var width = element.Box.Width.Value;
		var height = element.Box.Height.Value;
		var matrix = Affine.Identity;
		foreach (var (name, arguments) in Functions(value))
		{
			double Length(int index, double reference)
			{
				if (index >= arguments.Count)
				{
					return 0;
				}

				var length = CssLength.Parse(arguments[index]);
				return length.Resolve(reference, element.Computed.FontSize, element.Computed.ZeroWidth);
			}

			double Number(int index, double fallback)
			{
				return index < arguments.Count
					&& double.TryParse(arguments[index].Replace("deg", "", StringComparison.Ordinal),
						NumberStyles.Float,
						CultureInfo.InvariantCulture,
						out var parsed)
						? parsed
						: fallback;
			}

			matrix = matrix.Then(name switch
			{
				"translate" => Affine.Translate(Length(0, width), Length(1, height)),
				"translateX" => Affine.Translate(Length(0, width), 0),
				"translateY" => Affine.Translate(0, Length(0, height)),
				"rotate" => Affine.Rotate(Number(0, 0)),
				"scale" => Affine.Scale(Number(0, 1), Number(1, Number(0, 1))),
				_ => Affine.Identity
			});
		}

		var (originX, originY) = Origin(element, width, height);
		return Affine.Translate(originX, originY).Then(matrix).Then(Affine.Translate(-originX, -originY));
	}

	private static (double, double) Origin(Element element, double width, double height)
	{
		var value = element.Computed.Get("transform-origin", "50% 50%");
		if (value == "center")
		{
			return (width / 2, height / 2);
		}

		var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var x = parts.Length > 0 ? CssLength.Parse(parts[0]).Resolve(width, element.Computed.FontSize, 0) : width / 2;
		var y = parts.Length > 1 ? CssLength.Parse(parts[1]).Resolve(height, element.Computed.FontSize, 0) : height / 2;
		return (x, y);
	}

	private static IEnumerable<(string Name, List<string> Arguments)> Functions(string value)
	{
		var index = 0;
		while (index < value.Length)
		{
			var open = value.IndexOf('(', index);
			if (open < 0)
			{
				yield break;
			}

			var close = value.IndexOf(')', open);
			var name = value[index..open].Trim();
			var arguments = value[(open + 1)..close]
				.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
				.ToList();
			yield return (name, arguments);
			index = close + 1;
		}
	}

	public static Affine ToRoot(Element element)
	{
		var chain = new List<Element>();
		for (var current = element; current is not null; current = current.Parent)
		{
			chain.Add(current);
		}

		chain.Reverse();
		var matrix = Affine.Identity;
		Element? parent = null;
		foreach (var current in chain)
		{
			if (parent is not null)
			{
				matrix = matrix.Then(Affine.Translate(-parent.Box.ScrollLeft, -parent.Box.ScrollTop));
			}

			matrix = matrix
				.Then(Affine.Translate(current.Box.X.Value, current.Box.Y.Value))
				.Then(OwnTransform(current));
			parent = current;
		}

		return matrix;
	}

	public static RectD BoundingRect(Element element, Element root)
	{
		var matrix = ToRoot(element);
		var origin = ToRoot(root).Map(0, 0);
		var width = element.Box.Width.Value;
		var height = element.Box.Height.Value;
		var corners = new[]
		{
			matrix.Map(0, 0),
			matrix.Map(width, 0),
			matrix.Map(0, height),
			matrix.Map(width, height)
		};
		var left = corners.Min(point => point.X);
		var top = corners.Min(point => point.Y);
		var right = corners.Max(point => point.X);
		var bottom = corners.Max(point => point.Y);
		return new RectD(left - origin.X, top - origin.Y, right - left, bottom - top);
	}

	public static (LayoutUnit X, LayoutUnit Y) LayoutOffset(Element element)
	{
		var x = LayoutUnit.Zero;
		var y = LayoutUnit.Zero;
		for (var current = element; current is not null; current = current.Parent)
		{
			x += current.Box.X;
			y += current.Box.Y;
		}

		return (x, y);
	}
}
