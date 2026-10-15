namespace MacroDeckHost.Integrations.Mouse.Models;

public enum MouseCoordinateMode
{
	Current,

	Absolute,

	Relative
}

public readonly record struct MouseTarget(MouseCoordinateMode Mode, int X, int Y)
{
	public static readonly MouseTarget Current = new(MouseCoordinateMode.Current, 0, 0);

	public static MouseTarget At(int x, int y)
	{
		return new MouseTarget(MouseCoordinateMode.Absolute, x, y);
	}

	public static MouseTarget By(int dx, int dy)
	{
		return new MouseTarget(MouseCoordinateMode.Relative, dx, dy);
	}

	public bool NeedsCurrentPosition => Mode != MouseCoordinateMode.Absolute;

	public bool Moves => Mode != MouseCoordinateMode.Current;
}
