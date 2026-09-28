namespace MacroDeckHost.Integrations.System.Power;

internal static class MacOsPowerCommandResolver
{
	public static (string FileName, string[] Arguments) Resolve(PowerOperation operation, bool force)
		=> operation switch
		{
			PowerOperation.Sleep => ("pmset", ["sleepnow"]),

			PowerOperation.Restart =>
				("osascript", ["-e", "tell application \"System Events\" to restart"]),
			PowerOperation.ShutDown =>
				("osascript", ["-e", "tell application \"System Events\" to shut down"]),
			_ => throw new NotSupportedException($"{operation} has no macOS command.")
		};
}
