using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Lock;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LockStateReaderLinuxTests
{
	[Test]
	public void Factory_resolves_the_null_reader_reporting_not_supported()
	{
		var reader = LockStateReaderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(reader, Is.InstanceOf<NullLockStateReader>());
			Assert.That(reader.IsSupported, Is.False);
			Assert.That(reader.UnsupportedReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void IsLocked_never_reports_locked_so_execution_is_not_blocked()
	{
		var reader = LockStateReaderFactory.Create();

		Assert.That(reader.IsLocked(), Is.Null);
	}
}
