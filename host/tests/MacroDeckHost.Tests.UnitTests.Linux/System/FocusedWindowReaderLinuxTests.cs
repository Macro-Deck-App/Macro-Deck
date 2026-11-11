using System.Runtime.Versioning;
using MacroDeckHost.Platform.Focus;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class FocusedWindowReaderLinuxTests
{
	private static bool HasDisplay => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

	[Test]
	public void Support_is_reported_exactly_when_a_display_is_reachable_with_a_reason_when_not()
	{
		using var reader = new LinuxFocusedWindowReader();

		Assert.Multiple(() =>
		{
			Assert.That(reader.IsSupported, Is.EqualTo(HasDisplay));
			Assert.That(string.IsNullOrEmpty(reader.UnsupportedReason), Is.EqualTo(reader.IsSupported));
		});
	}

	[Test]
	public void Reading_without_a_display_returns_nothing_without_throwing()
	{
		using var reader = new LinuxFocusedWindowReader();

		FocusedAppInfo? info = null;
		Assert.DoesNotThrow(() => info = reader.Read());
		if (!HasDisplay)
		{
			Assert.That(info, Is.Null);
		}
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
