using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Logging;

public static class LogLevelDefaults
{
	public static LogEntryLevel ForChannel(BuildChannel channel)
		=> channel == BuildChannel.Development ? LogEntryLevel.Debug : LogEntryLevel.Information;
}
