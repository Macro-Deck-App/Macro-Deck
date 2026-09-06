namespace MacroDeckHost.Application.Logging;

public enum LogFileKind
{
	Host = 0,
	Bootstrapper = 1
}

public static class LogFileKindExtensions
{
	public static char Tag(this LogFileKind stream)
		=> stream == LogFileKind.Bootstrapper ? 'b' : 'h';

	public static bool TryFromTag(char tag, out LogFileKind stream)
	{
		switch (tag)
		{
			case 'h':
				stream = LogFileKind.Host;
				return true;
			case 'b':
				stream = LogFileKind.Bootstrapper;
				return true;
			default:
				stream = LogFileKind.Host;
				return false;
		}
	}
}
