using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>Progress is reported from the download loop while the worker may already have finished the
/// operation, so the two race. A failure must survive that race: a user who is told an install failed
/// must not watch it turn back into a download that never ends.</summary>
[TestFixture]
internal sealed class StoreOperationTrackerProgressTests
{
	[Test]
	public void A_completed_install_is_not_reported_as_cancelled_by_a_cancel_that_arrives_too_late()
	{
		var tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		var operation = tracker.Create(StoreOperationKind.Install,
			StoreExtensionKind.Plugin,
			"com.acme.hue",
			"1.0.0",
			"Hue Bridge",
			previousVersion: null);

		tracker.Transition(operation.Id, StoreOperationState.Completed);
		tracker.Transition(operation.Id, StoreOperationState.Cancelled);

		Assert.That(tracker.Find(operation.Id)!.State, Is.EqualTo(StoreOperationState.Completed));
	}

	[Test]
	public void A_failure_is_never_overwritten_by_progress_still_in_flight()
	{
		for (var attempt = 0; attempt < 50; attempt++)
		{
			var tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
			var operation = tracker.Create(StoreOperationKind.Install,
				StoreExtensionKind.Plugin,
				"com.acme.hue",
				"1.0.0",
				"Hue Bridge",
				previousVersion: null);

			using var start = new ManualResetEventSlim();
			var reporter = Task.Run(() =>
			{
				start.Wait();
				// A report where the byte count has reached the total bypasses the 250 ms throttle, so
				// every iteration really attempts a write - which is what makes the race reachable here.
				for (var i = 0; i < 400; i++)
				{
					tracker.ReportProgress(operation.Id, 4096, 4096);
				}
			});

			start.Set();
			tracker.Transition(operation.Id,
				StoreOperationState.Failed,
				StoreOperationError.ChecksumMismatch,
				"digest mismatch");
			reporter.Wait();

			var settled = tracker.Find(operation.Id);
			Assert.Multiple(() =>
			{
				Assert.That(settled!.State, Is.EqualTo(StoreOperationState.Failed));
				Assert.That(settled.Error, Is.EqualTo(StoreOperationError.ChecksumMismatch));
			});
		}
	}
}
