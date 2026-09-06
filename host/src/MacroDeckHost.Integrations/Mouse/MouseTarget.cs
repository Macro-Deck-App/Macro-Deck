namespace MacroDeckHost.Integrations.Mouse;

public enum MouseCoordinateMode
{
	Current,

	Absolute,

	Relative
}

public readonly record struct MouseTarget(MouseCoordinateMode Mode, int X, int Y)
{
	public static readonly MouseTarget Current = new(MouseCoordinateMode.Current, 0, 0);

	public static MouseTarget At(int x, int y) => new(MouseCoordinateMode.Absolute, x, y);

	public static MouseTarget By(int dx, int dy) => new(MouseCoordinateMode.Relative, dx, dy);

	public bool NeedsCurrentPosition => Mode != MouseCoordinateMode.Absolute;

	public bool Moves => Mode != MouseCoordinateMode.Current;
}
