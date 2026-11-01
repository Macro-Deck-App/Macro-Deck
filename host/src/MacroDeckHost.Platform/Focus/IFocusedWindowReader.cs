namespace MacroDeckHost.Platform.Focus;

public sealed record FocusedAppInfo(int ProcessId, string? ExecutablePath, string? ProcessName, string? BundleId);

public interface IFocusedWindowReader
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	bool IsDegraded => false;

	FocusedAppInfo? Read();
}
