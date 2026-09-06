namespace MacroDeckHost.Integrations.System.Power;

internal static class MacOsPowerCommandResolver
{
	// The Menu Extras CGSession binary, not the private login.framework SACLockScreenImmediate(): the
	// latter is real native "lock screen right now" but is private API with no public entry point, and
	// this repo does not call private frameworks. "-suspend" switches to the loginwindow (fast user
	// switching) rather than showing a lock overlay over the running session, which is effectively a
	// lock - the session stays open and needs the same password to get back to it.
	public const string CgSessionPath =
		"/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession";

	public static (string FileName, string[] Arguments) Resolve(PowerOperation operation, bool force)
		=> operation switch
		{
			PowerOperation.Lock => (CgSessionPath, ["-suspend"]),

			PowerOperation.Sleep => ("pmset", ["sleepnow"]),

			PowerOperation.Restart =>
				("osascript", ["-e", "tell application \"System Events\" to restart"]),
			PowerOperation.ShutDown =>
				("osascript", ["-e", "tell application \"System Events\" to shut down"]),
			_ => throw new NotSupportedException($"{operation} has no macOS command.")
		};
}
