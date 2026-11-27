namespace MacroDeckHost.Rendering.Layout;

// Chrome's fixed-point layout length: 1/64 px, converted from floats by truncation unless stated.
internal readonly record struct LayoutUnit(int Raw) : IComparable<LayoutUnit>
{
	public const int Denominator = 64;

	public static readonly LayoutUnit Zero = new(0);

	public static readonly LayoutUnit Max = new(int.MaxValue / 2);

	public double Value => Raw / (double)Denominator;

	public static LayoutUnit FromDouble(double value)
	{
		return new LayoutUnit(Clamp(Math.Truncate(value * Denominator)));
	}

	public static LayoutUnit FromDoubleRound(double value)
	{
		return new LayoutUnit(Clamp(Math.Round(value * Denominator, MidpointRounding.AwayFromZero)));
	}

	public static LayoutUnit FromDoubleCeil(double value)
	{
		return new LayoutUnit(Clamp(Math.Ceiling(value * Denominator)));
	}

	public static LayoutUnit FromDoubleFloor(double value)
	{
		return new LayoutUnit(Clamp(Math.Floor(value * Denominator)));
	}

	public static LayoutUnit FromInt(int value)
	{
		return new LayoutUnit(value * Denominator);
	}

	public static LayoutUnit operator +(LayoutUnit a, LayoutUnit b)
	{
		return new LayoutUnit(a.Raw + b.Raw);
	}

	public static LayoutUnit operator -(LayoutUnit a, LayoutUnit b)
	{
		return new LayoutUnit(a.Raw - b.Raw);
	}

	public static LayoutUnit operator -(LayoutUnit a)
	{
		return new LayoutUnit(-a.Raw);
	}

	public static LayoutUnit operator /(LayoutUnit a, int b)
	{
		return new LayoutUnit(a.Raw / b);
	}

	public static LayoutUnit operator *(LayoutUnit a, int b)
	{
		return new LayoutUnit(a.Raw * b);
	}

	public static bool operator <(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw < b.Raw;
	}

	public static bool operator >(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw > b.Raw;
	}

	public static bool operator <=(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw <= b.Raw;
	}

	public static bool operator >=(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw >= b.Raw;
	}

	public static LayoutUnit Min(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw <= b.Raw ? a : b;
	}

	public static LayoutUnit MaxOf(LayoutUnit a, LayoutUnit b)
	{
		return a.Raw >= b.Raw ? a : b;
	}

	public LayoutUnit ClampNegativeToZero()
	{
		return Raw < 0 ? Zero : this;
	}

	public int CompareTo(LayoutUnit other)
	{
		return Raw.CompareTo(other.Raw);
	}

	// Chrome's pixel snapping of a size at a location: the snapped far edge minus the snapped near edge.
	public static int SnapSizeToPixel(LayoutUnit size, LayoutUnit location)
	{
		var far = location + size;
		return RoundToInt(far) - RoundToInt(location);
	}

	public static int RoundToInt(LayoutUnit value)
	{
		return value.Raw >= 0
			? (value.Raw + Denominator / 2) / Denominator
			: -((-value.Raw + Denominator / 2 - 1) / Denominator);
	}

	public override string ToString()
	{
		return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static int Clamp(double raw)
	{
		if (double.IsNaN(raw))
		{
			return 0;
		}

		return (int)Math.Clamp(raw, int.MinValue / 2, int.MaxValue / 2);
	}
}
