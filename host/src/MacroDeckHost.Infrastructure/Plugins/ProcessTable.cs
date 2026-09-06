using System.ComponentModel;
using System.Diagnostics;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class ProcessTable : IProcessTable
{
	private readonly ILogger _logger;

	public ProcessTable(ILogger logger) => _logger = logger.ForContext<ProcessTable>();

	public RunningProcess? TryGet(int processId)
	{
		// On Unix a nonpositive pid is not an identifier: kill(0) hits our own process group and
		// kill(-1) every process this user owns, so it must never reach the OS.
		if (processId <= 0)
		{
			return null;
		}

		try
		{
			using var process = Process.GetProcessById(processId);
			if (process.HasExited)
			{
				return null;
			}

			return new RunningProcess(processId, process.StartTime.ToUniversalTime());
		}
		catch (ArgumentException)
		{
			return null;
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
		{
			PluginInfrastructureLog.ProcessLookupFailed(_logger, processId, ex);
			return null;
		}
	}

	public Task KillTree(int processId, CancellationToken cancellationToken = default)
	{
		if (processId <= 0)
		{
			return Task.CompletedTask;
		}

		try
		{
			using var process = Process.GetProcessById(processId);
			ProcessTreeTermination.KillTree(process, processId, _logger);
		}
		catch (ArgumentException)
		{
		}

		return Task.CompletedTask;
	}
}
