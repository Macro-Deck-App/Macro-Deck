namespace MacroDeckHost.Integrations.System.Power;

internal static class LinuxPowerCommandResolver
{
	public static (string FileName, string[] Arguments) ResolveLock(bool hasLoginctl)
		=> hasLoginctl ? ("loginctl", ["lock-session"]) : ("xdg-screensaver", ["lock"]);

	public static (string FileName, string[] Arguments) Resolve(PowerOperation operation, bool force, bool useSystemctl)
	{
		var verb = operation switch
		{
			PowerOperation.Sleep => "suspend",
			PowerOperation.Hibernate => "hibernate",
			PowerOperation.Restart => "reboot",
			PowerOperation.ShutDown => "poweroff",
			_ => throw new NotSupportedException($"{operation} has no systemctl/loginctl verb.")
		};

		var fileName = useSystemctl ? "systemctl" : "loginctl";
		var arguments = force ? new[] { verb, "-i" } : [verb];
		return (fileName, arguments);
	}
}
