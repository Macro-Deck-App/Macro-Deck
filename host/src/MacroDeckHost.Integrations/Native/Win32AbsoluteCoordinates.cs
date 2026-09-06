namespace MacroDeckHost.Integrations.Native;

internal static class Win32AbsoluteCoordinates
{
	public static int Normalize(int offset, int extent)
	{
		if (extent <= 0)
		{
			return 0;
		}

		var value = ((long)offset * 65536 + 32768) / extent;
		return (int)Math.Clamp(value, 0, 65535);
	}
}
