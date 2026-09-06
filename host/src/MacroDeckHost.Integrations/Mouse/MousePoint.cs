namespace MacroDeckHost.Integrations.Mouse;

public readonly record struct MousePoint(int X, int Y);

public readonly record struct MouseRect(int Left, int Top, int Width, int Height)
{
	public MousePoint Clamp(MousePoint point)
	{
		if (Width <= 0 || Height <= 0)
		{
			return point;
		}

		return new MousePoint(Math.Clamp(point.X, Left, Left + Width - 1),
			Math.Clamp(point.Y, Top, Top + Height - 1));
	}
}
