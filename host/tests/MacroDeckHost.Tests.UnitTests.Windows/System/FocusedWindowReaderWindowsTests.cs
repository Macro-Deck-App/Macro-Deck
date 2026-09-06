using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class FocusedWindowReaderWindowsTests
{
	[Test]
	public void Factory_returns_a_supported_reader()
	{
		var reader = FocusedWindowReaderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(reader, Is.InstanceOf<WindowsFocusedWindowReader>());
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
				Assert.That(info.ExecutablePath is not null || info.ProcessName is not null, Is.True);
				Assert.That(info.BundleId, Is.Null, "Windows has no concept of a bundle id");
			});
		}

		TestContext.Out.WriteLine($"Focused app: {info?.ExecutablePath ?? info?.ProcessName ?? "<none>"}");
	}
}
