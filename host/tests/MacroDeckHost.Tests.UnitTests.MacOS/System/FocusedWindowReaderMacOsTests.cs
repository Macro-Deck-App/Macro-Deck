using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class FocusedWindowReaderMacOsTests
{
	private const int ProcPidPathMaxSize = 4096; // PROC_PIDPATHINFO_MAXSIZE (4 * MAXPATHLEN)

	[Test]
	public void Factory_returns_a_supported_reader()
	{
		var reader = FocusedWindowReaderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(reader, Is.InstanceOf<MacOsFocusedWindowReader>());
			Assert.That(reader.IsSupported, Is.True);
			Assert.That(reader.UnsupportedReason, Is.Null);
		});
	}

	[Test]
	public void Read_marshals_without_throwing()
	{
		var reader = FocusedWindowReaderFactory.Create();

		FocusedAppInfo? info = null;
		Assert.DoesNotThrow(() => info = reader.Read());

		if (info is not null)
		{
			Assert.Multiple(() =>
			{
				Assert.That(info.ProcessId, Is.GreaterThan(0));
				Assert.That(
					info.BundleId is not null || info.ExecutablePath is not null || info.ProcessName is not null,
					Is.True);
			});
		}

		TestContext.Out.WriteLine($"Focused app: {info?.BundleId ?? info?.ExecutablePath ?? "<none>"}");
	}

	[Test]
	public void Read_ExecutablePath_matches_proc_pidpath_for_the_frontmost_application()
	{
		var reader = FocusedWindowReaderFactory.Create();

		var info = reader.Read();
		if (info?.ExecutablePath is null)
		{
			Assert.Pass("No frontmost application with an executable path was available in this environment.");
			return;
		}

		Assert.That(info.ExecutablePath, Is.EqualTo(ProcPidPath(info.ProcessId)));
	}

	private static string? ProcPidPath(int pid)
	{
		var buffer = new byte[ProcPidPathMaxSize];
		var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
		return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
	}

	[DllImport("/usr/lib/libSystem.B.dylib")]
	private static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);
}
