using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Integrations.Discord.Protocol;

internal sealed class DiscordIpcDenialLog
{
	private readonly ILogger _logger;

	private string? _warned;

	public DiscordIpcDenialLog(ILogger logger)
	{
		_logger = logger;
	}

	public void Record(DiscordIpcDenial? denial)
	{
		if (denial is null)
		{
			_warned = null;
			return;
		}

		var key = $"{denial.Endpoint}|{denial.Server?.ProcessId}|{denial.Cause}";
		Write(_logger, key == _warned ? LogEventLevel.Debug : LogEventLevel.Warning, denial);
		_warned = key;
	}

	public static void Write(ILogger logger, LogEventLevel level, DiscordIpcDenial denial)
	{
		logger.Write(level,
			"Access to Discord IPC endpoint {Endpoint} was denied ({Cause}): server process {ServerProcessId} "
			+ "{ServerProcessName}, server session {ServerSession}, own session {OwnSession}, server elevated "
			+ "{ServerElevated}, own elevated {OwnElevated}, same user {SameUser}",
			denial.Endpoint,
			denial.Cause,
			denial.Server?.ProcessId,
			denial.Server?.ProcessName,
			denial.Server?.SessionId,
			denial.Self?.SessionId,
			denial.Server?.Elevated,
			denial.Self?.Elevated,
			denial.SameUser);
	}
}
