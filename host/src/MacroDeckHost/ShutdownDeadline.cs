using System.Runtime.InteropServices;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Infrastructure.BackgroundServices;
using Serilog;

namespace MacroDeckHost;

internal static class ShutdownDeadline
{
	private static readonly TimeSpan _lastResortDelay = TimeSpan.FromSeconds(5);

	public static void Arm(IHost host)
	{
		Arm(host.Services.GetRequiredService<IHostApplicationLifetime>(),
			host.Services.GetRequiredService<IApplicationRestartService>(),
			PluginShutdownBudgets.HostExitDeadline,
			_lastResortDelay,
			FlushAndExit,
			Terminate);
	}

	public static void Arm(
		IHostApplicationLifetime lifetime,
		IApplicationRestartService restart,
		TimeSpan deadline,
		TimeSpan lastResortDelay,
		Action<int> exit,
		Action<int> terminate)
	{
		// Waits on the handle instead of registering a callback: callbacks run in sequence inside
		// StopApplication, so one that blocks would keep a later one from ever starting the clock.
		var stopping = lifetime.ApplicationStopping.WaitHandle;
		StartBackgroundThread("Host exit deadline",
			() =>
			{
				stopping.WaitOne();
				Thread.Sleep(deadline);

				var exitCode = restart.RestartRequested ? HostExitCodes.RestartRequested : 0;
				StartBackgroundThread("Host exit last resort",
					() =>
					{
						Thread.Sleep(lastResortDelay);
						terminate(exitCode);
					});

				Log.Error("The host did not finish shutting down within {Deadline}; exiting with code {ExitCode}",
					deadline,
					exitCode);
				exit(exitCode);
			});
	}

	private static void StartBackgroundThread(string name, ThreadStart body)
	{
		var thread = new Thread(body)
		{
			IsBackground = true,
			Name = name
		};
		thread.Start();
	}

	private static void FlushAndExit(int exitCode)
	{
		Log.CloseAndFlush();
		Environment.Exit(exitCode);
	}

	// Ends the process without any managed shutdown: Environment.Exit can itself wait forever on
	// a blocked runtime, and no BCL call ends the process at once with a chosen exit code.
	private static void Terminate(int exitCode)
	{
		if (OperatingSystem.IsWindows())
		{
			TerminateProcess(GetCurrentProcess(), (uint)exitCode);
			return;
		}

		_exit(exitCode);
	}

	[DllImport("libc")]
	private static extern void _exit(int status);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
