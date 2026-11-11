using System.Globalization;
using System.Runtime.InteropServices;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.System;

// The bootstrapper starts the host with process_group(0) and stops it with SIGKILL to that group
// (ui/bootstrapper/src/host.rs). The Windows job-object half is covered by host_job.rs tests.
[Platform("Linux,MacOsX")]
[Category("X11")]
[Category("MacOS")]
public sealed class LaunchedAppSurvivesHostExitTests
{
	private const int SigKill = 9;

	[Test]
	public void An_app_started_by_an_action_survives_the_host_being_stopped_for_a_quit_or_restart()
	{
		var pidFile = Path.Combine(Path.GetTempPath(), $"macrodeck-launched-{Guid.NewGuid():N}.pid");
		var app = ProbeLocator.Executable("mdprobe-app");
		int? appPid = null;
		try
		{
			using (var host = ProbeProcess.Start("mdprobe-host",
				"launch",
				"--own-group",
				"true",
				"--",
				app,
				"idle",
				"--pid-file",
				pidFile))
			{
				host.WaitFor(e => e.Type == "launched", TimeSpan.FromSeconds(20), "the launch");
				appPid = WaitForPid(pidFile);
				Assert.That(kill(-host.Pid, SigKill), Is.Zero, "stop the host's process group");
				Assert.That(() => host.HasExited, Is.True.After(5000, 50), "the host is gone");
			}

			Thread.Sleep(500);

			Assert.That(IsAlive(appPid.Value), Is.True, "the launched app survived the host");
		}
		finally
		{
			if (appPid is { } pid)
			{
				_ = kill(pid, SigKill);
			}

			File.Delete(pidFile);
		}
	}

	private static int WaitForPid(string pidFile)
	{
		var deadline = DateTime.UtcNow.AddSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			if (File.Exists(pidFile)
				&& int.TryParse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture, out var pid))
			{
				return pid;
			}

			Thread.Sleep(50);
		}

		Assert.Fail("The launched app never started.");
		return 0;
	}

	private static bool IsAlive(int pid)
	{
		try
		{
			using var process = global::System.Diagnostics.Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	[DllImport("libc", SetLastError = true)]
	private static extern int kill(int pid, int signal);
}
