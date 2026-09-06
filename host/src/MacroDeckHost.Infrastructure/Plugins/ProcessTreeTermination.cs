using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

internal static class ProcessTreeTermination
{
	public static void KillTree(Process process, int processId, ILogger logger)
	{
		try
		{
			if (!process.HasExited)
			{
				PluginInfrastructureLog.KillingProcessTree(logger, processId);
				process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
			// Already exited: a benign race between the HasExited check above and the OS reaping it.
		}
		catch (Exception ex) when (ex is Win32Exception or AggregateException or NotSupportedException)
		{
			PluginInfrastructureLog.KillTreeFallback(logger, processId, ex);
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: false);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception fallbackEx) when (fallbackEx is Win32Exception or NotSupportedException)
			{
				PluginInfrastructureLog.KillFailed(logger, processId, fallbackEx);
			}
		}
	}
}
