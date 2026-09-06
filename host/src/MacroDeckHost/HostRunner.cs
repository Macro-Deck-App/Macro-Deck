using MacroDeckHost.Application.Lifecycle;
using Serilog;

namespace MacroDeckHost;

public static class HostRunner
{
	// RunAsync disposes the host - and with it the service provider - before it returns, so the restart
	// service has to be resolved while the provider is still alive. Reading it afterwards throws
	// ObjectDisposedException, which used to cost the restart exit code and made the shell report a crash.
	public static async Task<int> RunAsync(IHost host)
	{
		var restart = host.Services.GetRequiredService<IApplicationRestartService>();
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

		try
		{
			await host.RunAsync();
		}
		catch (Exception e) when (lifetime.ApplicationStarted.IsCancellationRequested)
		{
			// Teardown failed on a host that was already serving - a hosted service that throws, or the
			// generic host's stop timeout expiring while plugins are still being terminated. The process
			// is going down either way; reporting it as a crash would cost an intentional restart its
			// exit code and greet the user with the unexpected-shutdown dialog after a plain quit.
			Log.Error(e, "The host did not shut down cleanly");
		}

		return restart.RestartRequested ? HostExitCodes.RestartRequested : 0;
	}
}
