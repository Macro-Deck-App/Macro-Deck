namespace MacroDeckHost.Platform.Native;

internal sealed class X11DisplayLostException : Exception
{
	public X11DisplayLostException()
		: base("The X11 display was lost.")
	{
	}
}
