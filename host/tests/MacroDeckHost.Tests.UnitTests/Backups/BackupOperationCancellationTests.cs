using MacroDeckHost.Infrastructure.Backups;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupOperationCancellationTests
{
	[Test]
	public void Cancelling_a_running_operation_cancels_its_work()
	{
		var cancellation = new BackupOperationCancellation();
		var operationId = Guid.NewGuid();
		using var scope = cancellation.Begin(operationId, CancellationToken.None);

		var cancelled = cancellation.Cancel(operationId);

		Assert.Multiple(() =>
		{
			Assert.That(cancelled, Is.True);
			Assert.That(scope.Token.IsCancellationRequested, Is.True);
			Assert.That(scope.IsCancelled, Is.True);
		});
	}

	[Test]
	public void A_finished_operation_can_no_longer_be_cancelled()
	{
		var cancellation = new BackupOperationCancellation();
		var operationId = Guid.NewGuid();
		cancellation.Begin(operationId, CancellationToken.None).Dispose();

		Assert.That(cancellation.Cancel(operationId), Is.False);
	}

	[Test]
	public void The_callers_own_cancellation_is_not_mistaken_for_a_user_cancel()
	{
		var cancellation = new BackupOperationCancellation();
		using var caller = new CancellationTokenSource();
		using var scope = cancellation.Begin(Guid.NewGuid(), caller.Token);

		caller.Cancel();

		Assert.Multiple(() =>
		{
			Assert.That(scope.Token.IsCancellationRequested, Is.True);
			Assert.That(scope.IsCancelled, Is.False);
		});
	}
}
