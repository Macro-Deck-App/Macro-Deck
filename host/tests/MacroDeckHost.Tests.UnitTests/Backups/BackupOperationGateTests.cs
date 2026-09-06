using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupOperationGateTests
{
	private BackupOperationGate _gate = null!;

	[SetUp]
	public void SetUp() => _gate = new BackupOperationGate();

	[TearDown]
	public void TearDown() => _gate.Dispose();

	[Test]
	public void RefusesASecondOperationWhileOneIsRunning()
	{
		using var first = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual).Data!;

		var second = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual);
		var restore = _gate.TryAcquire(BackupOperationKind.Restore, BackupTrigger.BeforeRestore);

		Assert.Multiple(() =>
		{
			Assert.That(second.Success, Is.False);
			Assert.That(second.Error, Is.EqualTo(BackupError.Busy));
			Assert.That(restore.Success, Is.False, "a restore must not start while a backup is running");
		});
	}

	[Test]
	public void FreesTheGateWhenTheLeaseIsReleased()
	{
		var first = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual);
		first.Data!.Dispose();

		var second = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual);

		Assert.That(second.Success, Is.True);
		second.Data!.Dispose();
	}

	[Test]
	public void ReleasingTheSameLeaseTwiceDoesNotHandOutTwoSlots()
	{
		var lease = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual).Data!;
		lease.Dispose();
		lease.Dispose();

		using var held = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual).Data!;
		var second = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual);

		Assert.Multiple(() =>
		{
			Assert.That(held, Is.Not.Null);
			Assert.That(second.Success, Is.False, "a double release must not let two operations run at once");
		});
	}

	// Restore prepare marks the restore pending and then takes its own safety backup, so BeforeRestore is
	// the one trigger a pending restore still has to admit.
	[Test]
	public void APendingRestoreBlocksEverythingButItsOwnSafetyBackup()
	{
		_gate.RestorePending = true;

		var manual = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual);
		var scheduled = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Scheduled);
		var pluginUpdate = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.BeforePluginUpdate);
		var hostUpdate = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.BeforeHostUpdate);
		var safety = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.BeforeRestore);

		Assert.Multiple(() =>
		{
			Assert.That(manual.Error, Is.EqualTo(BackupError.RestorePending));
			Assert.That(scheduled.Error, Is.EqualTo(BackupError.RestorePending));
			Assert.That(pluginUpdate.Error, Is.EqualTo(BackupError.RestorePending));
			Assert.That(hostUpdate.Error,
				Is.EqualTo(BackupError.RestorePending),
				"a host update must not proceed over data a restore is about to replace");
			Assert.That(safety.Success, Is.True);
		});
	}

	[Test]
	public async Task LetsAWaitingTriggerThroughOnceTheGateIsFree()
	{
		var first = _gate.TryAcquire(BackupOperationKind.Create, BackupTrigger.Manual).Data!;

		var waiting = _gate.Acquire(BackupOperationKind.Create,
			BackupTrigger.BeforeHostUpdate,
			TimeSpan.FromSeconds(5));

		first.Dispose();
		var result = await waiting;

		Assert.That(result.Success, Is.True);
		result.Data!.Dispose();
	}
}
