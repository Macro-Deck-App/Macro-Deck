using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class FocusedWindowReaderLinuxTests
{
	[Test]
	public void No_display_reports_unsupported_with_a_reason()
	{
		using var reader = new LinuxFocusedWindowReader();

		Assert.Multiple(() =>
		{
			Assert.That(reader.IsSupported, Is.False);
			Assert.That(reader.UnsupportedReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void No_display_read_returns_null_without_throwing()
	{
		using var reader = new LinuxFocusedWindowReader();

		FocusedAppInfo? info = null;
		Assert.DoesNotThrow(() => info = reader.Read());
		Assert.That(info, Is.Null);
	}

	[Test]
	public void Factory_returns_the_linux_reader()
	{
		var reader = FocusedWindowReaderFactory.Create();
		using (reader as IDisposable)
		{
			Assert.That(reader, Is.InstanceOf<LinuxFocusedWindowReader>());
		}
	}
}
