using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Lock;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class LockStateReaderWindowsTests
{
	[Test]
	public void Factory_returns_a_supported_reader()
	{
		var reader = LockStateReaderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(reader, Is.InstanceOf<WindowsLockStateReader>());
			Assert.That(reader.IsSupported, Is.True);
			Assert.That(reader.UnsupportedReason, Is.Null);
		});
	}

	[Test]
	public void IsLocked_returns_a_value_without_throwing()
	{
		var reader = LockStateReaderFactory.Create();

		bool? locked = null;
		Assert.DoesNotThrow(() => locked = reader.IsLocked());

		Assert.That(locked, Is.Not.Null);
		TestContext.Out.WriteLine($"IsLocked: {locked}");
	}
}
