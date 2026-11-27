namespace MacroDeckHost.Integrations.Clipboard.Native;

internal enum ClipboardUnavailableReason
{
	None,
	UnsupportedOs,
	NoDesktopSession,
	ToolMissing
}

internal sealed record ClipboardAvailability(bool IsAvailable, ClipboardUnavailableReason Reason, string? MissingTool)
{
	public static ClipboardAvailability Available { get; } = new(true, ClipboardUnavailableReason.None, null);

	public static ClipboardAvailability Unavailable(ClipboardUnavailableReason reason, string? missingTool = null)
	{
		return new ClipboardAvailability(false, reason, missingTool);
	}
}
