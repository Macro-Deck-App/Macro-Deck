using MacroDeckHost.Application.Plugins.IconPacks;

namespace MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;

[TestFixture]
internal sealed class PluginIconPackReadinessTests
{
	private const string PluginId = "com.example.logos";

	[Test]
	public async Task A_sync_requested_before_the_caches_are_ready_waits_for_them_and_adds_the_pack_once()
	{
		using var host = new PluginIconPackTestHost(ready: false);
		var archive = await host.BuildArchive("Logos", ("home", "one"));

		var sync = host.SyncDevelopment(PluginId, ("logos", archive));
		await Task.Delay(100);
		var finishedEarly = sync.IsCompleted;
		host.Readiness.MarkCachesReady();
		host.Readiness.MarkIconPacksReady();
		var result = await sync;

		Assert.Multiple(() =>
		{
			Assert.That(finishedEarly, Is.False);
			Assert.That(result.Status, Is.EqualTo(PluginIconPackSyncStatus.Synced));
			Assert.That(host.PluginPack(PluginId, "logos"), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_failed_icon_pack_initialization_skips_the_sync_without_waiting()
	{
		using var host = new PluginIconPackTestHost(ready: false);
		var archive = await host.BuildArchive("Logos", ("home", "one"));
		host.Readiness.MarkCachesReady();
		host.Readiness.MarkIconPacksFailed(new IOException("unreadable"));

		var result = await host.SyncDevelopment(PluginId, ("logos", archive)).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PluginIconPackSyncStatus.Skipped));
			Assert.That(host.PluginPack(PluginId, "logos"), Is.Null);
		});
	}

	[Test]
	public async Task A_sync_gives_up_waiting_after_the_bound_and_runs_once_readiness_arrives_later()
	{
		using var host = new PluginIconPackTestHost(ready: false, readinessBound: TimeSpan.FromMilliseconds(200));
		var archive = await host.BuildArchive("Logos", ("home", "one"));

		var first = await host.SyncDevelopment(PluginId, ("logos", archive)).WaitAsync(TimeSpan.FromSeconds(5));
		var second = await host.SyncDevelopment(PluginId, ("logos", archive)).WaitAsync(TimeSpan.FromMilliseconds(100));
		host.Readiness.MarkCachesReady();
		host.Readiness.MarkIconPacksReady();

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (host.PluginPack(PluginId, "logos") is null && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		Assert.Multiple(() =>
		{
			Assert.That(first.Status, Is.EqualTo(PluginIconPackSyncStatus.Skipped));
			Assert.That(second.Status, Is.EqualTo(PluginIconPackSyncStatus.Skipped), "the bound is spent once, not per call");
			Assert.That(host.PluginPack(PluginId, "logos"), Is.Not.Null, "the skipped sync runs when readiness arrives");
		});
	}
}
