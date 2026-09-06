namespace MacroDeckHost.Integrations.System.Focus;

public sealed class NullFocusedWindowReader : IFocusedWindowReader
{
	public bool IsSupported => false;

	public string? UnsupportedReason => "Focused-application detection is not supported on this operating system.";

	public FocusedAppInfo? Read() => null;
}
