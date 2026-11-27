using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Platform.Processes;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Clipboard;

[Platform("Linux")]
[Category("Wayland")]
[SupportedOSPlatform("linux")]
public sealed class WaylandClipboardTests : ClipboardBackendCases
{
	internal override IClipboardBackend CreateBackend()
	{
		Interactive.Require(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
			"No Wayland compositor (WAYLAND_DISPLAY is not set).");
		return new LinuxClipboardBackend(true, ProcessRunner.CommandExists);
	}

	[Test]
	public async Task Watching_on_weston_which_lacks_data_control_reports_unsupported_once_and_stops()
	{
		using var source = new WaylandClipboardChangeSource();
		var unsupported = 0;
		var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		source.WatchUnsupported += () =>
		{
			Interlocked.Increment(ref unsupported);
			reported.TrySetResult();
		};

		source.Start();
		await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Task.Delay(WaylandClipboardChangeSource.StartupWindow * 2);

		Assert.Multiple(() =>
		{
			Assert.That(source.IsWatchUnsupported, Is.True);
			Assert.That(Volatile.Read(ref unsupported), Is.EqualTo(1));
		});
	}
}
