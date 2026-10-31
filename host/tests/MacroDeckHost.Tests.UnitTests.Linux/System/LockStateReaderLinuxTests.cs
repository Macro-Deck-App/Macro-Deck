using System.Runtime.Versioning;
using MacroDeckHost.Platform.Lock;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LockStateReaderLinuxTests
{
	[Test]
	public void Factory_resolves_the_logind_reader_on_any_machine()
	{
		var reader = LockStateReaderFactory.Create();

		Assert.That(reader, Is.InstanceOf<LinuxLockStateReader>());
		Assert.DoesNotThrow(() => reader.IsLocked());
	}

	[Test]
	public void A_session_reporting_the_hint_is_locked_and_follows_it()
	{
		var hint = true;
		var reader = new LinuxLockStateReader(() => hint);

		Assert.Multiple(() =>
		{
			Assert.That(reader.IsSupported, Is.True);
			Assert.That(reader.UnsupportedReason, Is.Null);
			Assert.That(reader.IsLocked(), Is.True);
		});

		hint = false;

		Assert.That(reader.IsLocked(), Is.False);
	}

	[Test]
	public void A_machine_without_a_logind_answer_is_unsupported_and_never_locked()
	{
		var reader = new LinuxLockStateReader(() => null);

		Assert.Multiple(() =>
		{
			Assert.That(reader.IsSupported, Is.False);
			Assert.That(reader.UnsupportedReason, Is.Not.Null.And.Not.Empty);
			Assert.That(reader.IsLocked(), Is.Null);
		});
	}

	[Test]
	public void A_read_failing_after_locked_unlocks_so_the_lock_never_sticks()
	{
		bool? hint = true;
		var reader = new LinuxLockStateReader(() => hint);
		Assert.That(reader.IsLocked(), Is.True);

		hint = null;

		Assert.That(reader.IsLocked(), Is.False);
	}
}
