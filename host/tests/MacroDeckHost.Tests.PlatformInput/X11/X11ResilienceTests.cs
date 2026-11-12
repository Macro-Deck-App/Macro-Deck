using System.Diagnostics;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.X11;

[Platform("Linux")]
[Category("X11")]
public sealed class X11ResilienceTests
{
	private const string PrivateDisplay = ":97";

	[Test]
	[Ignore("Red today: #1710 the host exits when its X server goes away")]
	public void The_host_stays_alive_when_the_x_server_goes_away()
	{
		using var server = StartPrivateServer();
		using var host = ProbeProcess.Start("mdprobe-x11",
			new Dictionary<string, string> { ["DISPLAY"] = PrivateDisplay },
			"x11-stress",
			"--scenario",
			"survive");
		host.WaitFor(e => e.Type == "ready", TimeSpan.FromSeconds(20), "the providers");

		server.Kill();
		server.WaitForExit(5000);
		host.Send("press");

		host.WaitFor(e => e.Type == "alive" || e.Type == "error", TimeSpan.FromSeconds(10), "the press after the loss");
		Assert.That(host.HasExited, Is.False, host.Diagnostics);
	}

	[Test]
	public void A_thousand_parallel_targeted_and_global_presses_all_arrive_without_xlib_errors()
	{
		using var window = ProbeProcess.StartWindow("mdprobe-par");
		ProbeFocus.Focus(window);
		window.Settle();
		window.Clear();

		using var host = ProbeProcess.Start("mdprobe-x11",
			"x11-stress",
			"--scenario",
			"parallel",
			"--target",
			window.Name,
			"--count",
			"1000");
		host.WaitFor(e => e.Type == "done" || e.Type == "error", TimeSpan.FromMinutes(3), "the presses");
		window.Settle(1000, 30000);

		var text = window.Text;
		Assert.Multiple(() =>
		{
			Assert.That(host.Events.Any(e => e.Type == "error"), Is.False, host.Diagnostics);
			Assert.That(host.StandardError.Where(line => line.Contains("Xlib", StringComparison.Ordinal)), Is.Empty);
			Assert.That(text.Count(c => c == 'a'), Is.EqualTo(500), "global presses");
			Assert.That(text.Count(c => c == 'b'), Is.EqualTo(500), "targeted presses");
		});
	}

	private static Process StartPrivateServer()
	{
		var server = Process.Start(new ProcessStartInfo("Xvfb", $"{PrivateDisplay} -screen 0 1024x768x24 -nolisten tcp")
			{
				UseShellExecute = false
			})
			?? throw new InvalidOperationException("Xvfb did not start");
		var socket = "/tmp/.X11-unix/X" + PrivateDisplay.TrimStart(':');
		Assert.That(() => File.Exists(socket), Is.True.After(10000, 50), "the private X server");
		return server;
	}
}
