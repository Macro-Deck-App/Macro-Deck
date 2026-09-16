using MacroDeckHost.Application.Store;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreRegistryRefreshTrackerTests
{
	[Test]
	public void File_progress_is_published_at_a_bounded_rate_but_the_last_file_always_arrives()
	{
		var time = new ManualTimeProvider();
		var tracker = new StoreRegistryRefreshTracker(time);
		var published = new List<StoreRegistryRefreshRun>();
		tracker.Changed += published.Add;

		tracker.Begin(StoreRegistryRefreshTrigger.Manual);
		tracker.ReportProgress(1, 10);
		tracker.ReportProgress(2, 10);
		time.Now += TimeSpan.FromMilliseconds(300);
		tracker.ReportProgress(3, 10);
		tracker.ReportProgress(10, 10);

		Assert.That(published.Skip(1).Select(run => run.FilesCompleted), Is.EqualTo(new[] { 1, 3, 10 }));
	}

	[Test]
	public void Every_published_snapshot_is_newer_than_the_last_even_across_runs()
	{
		var tracker = new StoreRegistryRefreshTracker(new ManualTimeProvider());
		var published = new List<StoreRegistryRefreshRun>();
		tracker.Changed += published.Add;

		var first = tracker.Begin(StoreRegistryRefreshTrigger.Manual);
		tracker.Log(StoreRegistryRefreshStep.FetchingManifest);
		tracker.Finish(StoreRegistryRefreshRunState.Succeeded, null, null, StoreRegistryStatus.Unavailable);
		tracker.Log(StoreRegistryRefreshStep.Verifying);
		var second = tracker.Begin(StoreRegistryRefreshTrigger.Scheduled);

		Assert.Multiple(() =>
		{
			Assert.That(published.Select(run => run.Revision), Is.Ordered.Ascending.And.Unique);
			Assert.That(published, Has.Count.EqualTo(4));
			Assert.That(second.Id, Is.Not.EqualTo(first.Id));
			Assert.That(second.HostInstanceId, Is.EqualTo(first.HostInstanceId));
			Assert.That(second.Entries.Select(entry => entry.Step), Is.EqualTo(new[] { StoreRegistryRefreshStep.Started }));
		});
	}
}
