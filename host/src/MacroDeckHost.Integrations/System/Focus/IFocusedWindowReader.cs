namespace MacroDeckHost.Integrations.System.Focus;

public sealed record FocusedAppInfo(int ProcessId, string? ExecutablePath, string? ProcessName, string? BundleId);

public interface IFocusedWindowReader
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	FocusedAppInfo? Read();
}
