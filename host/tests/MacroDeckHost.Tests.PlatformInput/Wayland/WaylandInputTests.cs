using System.Diagnostics;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Wayland;

[Platform("Linux")]
[Category("Wayland")]
public sealed class WaylandInputTests
{
	[OneTimeSetUp]
	public void RequireWaylandSession()
	{
		Interactive.Require(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
			"No Wayland compositor (WAYLAND_DISPLAY is not set).");
	}

	[Test]
	public async Task An_x11_window_under_xwayland_receives_typed_text()
	{
		var provider = KeyboardInputProviderFactory.Create();
		try
		{
			var keyboard = new KeyboardInputService(provider, new KeyboardLayoutService());
			using var probe = ProbeProcess.StartWindow("mdprobe-xwl");
			ProbeFocus.Focus(probe);
			probe.Settle();
			probe.Clear();

			await keyboard.TypeTextAsync("wayland");
			probe.Settle();

			Assert.Multiple(() =>
			{
				Assert.That(probe.Text, Is.EqualTo("wayland"), probe.Diagnostics);
				Assert.That(InputState.HeldKeys(), Is.Empty);
			});
		}
		finally
		{
			(provider as IDisposable)?.Dispose();
		}
	}

	[Test]
	public async Task A_native_wayland_window_receives_no_injected_input()
	{
		var seatDisplay = Environment.GetEnvironmentVariable("LINUX_DISPLAY_X11");
		Interactive.Require(!string.IsNullOrEmpty(seatDisplay),
			"No nested X display feeds the compositor's seat (LINUX_DISPLAY_X11 is not set).");
		var keyboardProvider = KeyboardInputProviderFactory.Create();
		var mouseProvider = MouseInputProviderFactory.Create();
		using var native = StartEventDemo(out var output);
		try
		{
			using (var seat = ProbeProcess.Start("mdprobe-seat",
				new Dictionary<string, string> { ["DISPLAY"] = seatDisplay! },
				"x11-stress",
				"--scenario",
				"survive"))
			{
				seat.WaitFor(e => e.Type == "ready", TimeSpan.FromSeconds(20), "the seat input sender");
				seat.Send("press");
				Assert.That(() => InputLines(output).Count,
					Is.GreaterThan(0).After(10000, 100),
					"input through the compositor's own seat reaches the native window");
			}

			Thread.Sleep(500);
			var before = InputLines(output).Count;

			await new KeyboardInputService(keyboardProvider, new KeyboardLayoutService()).TypeTextAsync("native");
			var mouse = new MouseInputService(mouseProvider);
			await mouse.ClickAsync(MouseButton.Left, 1, MouseTarget.At(200, 200));
			await mouse.ScrollAsync(ScrollAxis.Vertical, -2);
			Thread.Sleep(1500);

			Assert.That(InputLines(output).Skip(before), Is.Empty, "XTest input reaches only X11 clients");
		}
		finally
		{
			native.Kill();
			(keyboardProvider as IDisposable)?.Dispose();
			(mouseProvider as IDisposable)?.Dispose();
		}
	}

	private static Process StartEventDemo(out List<string> output)
	{
		var lines = new List<string>();
		var native = Process.Start(new ProcessStartInfo("stdbuf",
				"-oL weston-eventdemo --log-key --log-button --log-axis")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false
			})
			?? throw new InvalidOperationException("weston-eventdemo did not start");
		native.OutputDataReceived += (_, line) => Record(lines, line.Data);
		native.ErrorDataReceived += (_, line) => Record(lines, line.Data);
		native.BeginOutputReadLine();
		native.BeginErrorReadLine();
		Thread.Sleep(1000);
		Assert.That(native.HasExited, Is.False, "weston-eventdemo must be running");
		output = lines;
		return native;
	}

	private static List<string> InputLines(List<string> output)
	{
		lock (output)
		{
			return output.Where(line => line.StartsWith("key ", StringComparison.Ordinal)
					|| line.StartsWith("button ", StringComparison.Ordinal)
					|| line.StartsWith("axis ", StringComparison.Ordinal))
				.ToList();
		}
	}

	[Test]
	public async Task The_keyboard_and_mouse_integrations_report_that_wayland_limits_input()
	{
		var keyboardIssues = await new KeyboardInputIntegration().GetIssuesAsync();
		var mouseIssues = await new MouseInputIntegration().GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(keyboardIssues.Select(i => (i.Id, i.Severity)),
				Does.Contain(("wayland-input-limited", IntegrationIssueSeverity.Warning)));
			Assert.That(mouseIssues.Select(i => (i.Id, i.Severity)),
				Does.Contain(("positioning-unreliable", IntegrationIssueSeverity.Warning)));
		});
	}

	private static void Record(List<string> output, string? line)
	{
		if (line is null)
		{
			return;
		}

		lock (output)
		{
			output.Add(line);
		}
	}
}
