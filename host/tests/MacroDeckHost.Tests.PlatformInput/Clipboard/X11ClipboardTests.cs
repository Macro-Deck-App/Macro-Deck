using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Platform.Processes;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Clipboard;

[Platform("Linux")]
[Category("X11")]
[SupportedOSPlatform("linux")]
public sealed class X11ClipboardTests : ClipboardBackendCases
{
	private const string PrivateDisplay = ":96";

	internal override IClipboardBackend CreateBackend()
	{
		Interactive.Require(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
			"No X display (DISPLAY is not set).");
		return new LinuxClipboardBackend(false, ProcessRunner.CommandExists);
	}

	[Test]
	public async Task Another_application_taking_the_clipboard_is_reported_as_a_change()
	{
		using var source = new X11ClipboardChangeSource();
		using var changes = new SemaphoreSlim(0);
		source.Changed += _ => changes.Release();
		source.Start();
		await Task.Delay(500);

		await CopyWithXclipAsync(Environment.GetEnvironmentVariable("DISPLAY")!, "from another application");

		Assert.Multiple(() =>
		{
			Assert.That(changes.Wait(TimeSpan.FromSeconds(5)), Is.True, "an XFixes selection notification");
			Assert.That(source.IsWatchUnsupported, Is.False);
		});
	}

	[Test]
	public async Task Watching_survives_the_x_server_going_away_and_resumes_on_the_next_one()
	{
		using var source = new X11ClipboardChangeSource(PrivateDisplay);
		using var changes = new SemaphoreSlim(0);
		source.Changed += _ => changes.Release();

		using (var first = StartPrivateServer())
		{
			source.Start();
			await Task.Delay(1000);
			await CopyWithXclipAsync(PrivateDisplay, "before the loss");
			Assert.That(changes.Wait(TimeSpan.FromSeconds(5)), Is.True, "a change on the first server");

			Terminate(first);
		}

		await Task.Delay(1500);
		using var second = StartPrivateServer();
		try
		{
			await Task.Delay(3000);
			await CopyWithXclipAsync(PrivateDisplay, "after the loss");

			Assert.Multiple(() =>
			{
				Assert.That(changes.Wait(TimeSpan.FromSeconds(10)), Is.True, "a change on the restarted server");
				Assert.That(source.IsWatchUnsupported, Is.False);
			});
		}
		finally
		{
			Terminate(second);
		}
	}

	private static async Task CopyWithXclipAsync(string display, string text)
	{
		var startInfo = new ProcessStartInfo("/bin/sh")
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
			Environment = { ["DISPLAY"] = display }
		};
		startInfo.ArgumentList.Add("-c");
		startInfo.ArgumentList.Add("exec xclip -selection clipboard -i >/dev/null 2>&1");
		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("xclip did not start");
		await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(text));
		process.StandardInput.Close();
		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(process.ExitCode, Is.Zero, "xclip");
	}

	// A terminated Xvfb drops its clients just like a crash but removes its socket and lock, so it can restart.
	private static void Terminate(Process server)
	{
		using var kill = Process.Start("kill", ["-TERM", server.Id.ToString(CultureInfo.InvariantCulture)]);
		_ = kill?.WaitForExit(5000);
		if (!server.WaitForExit(5000))
		{
			server.Kill();
		}
	}

	private static Process StartPrivateServer()
	{
		var socket = "/tmp/.X11-unix/X" + PrivateDisplay.TrimStart(':');
		Assert.That(() => !File.Exists(socket), Is.True.After(10000, 50), "the previous private X server is gone");
		var server = Process.Start(new ProcessStartInfo("Xvfb", $"{PrivateDisplay} -screen 0 1024x768x24 -nolisten tcp")
			{
				UseShellExecute = false
			})
			?? throw new InvalidOperationException("Xvfb did not start");
		Assert.That(() => File.Exists(socket), Is.True.After(10000, 50), "the private X server");
		return server;
	}
}
