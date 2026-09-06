using System.Diagnostics;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginProcessJournalTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private PluginProcessJournal CreateJournal() => new(_paths, Log.Logger);

	private static PluginProcessJournalEntry Entry(string launchId,
		string pluginId,
		int processId,
		DateTimeOffset startedAt) => new()
	{
		LaunchId = launchId,
		PluginId = pluginId,
		ProcessId = processId,
		StartedAt = startedAt
	};

	[Test]
	public void A_recorded_launch_round_trips_through_a_fresh_journal()
	{
		var startedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
		CreateJournal().Record(Entry("launch-1", "com.example.plugin", 1234, startedAt)).GetAwaiter().GetResult();

		var reloaded = CreateJournal().Load().Entries.Single();

		Assert.Multiple(() =>
		{
			Assert.That(reloaded.LaunchId, Is.EqualTo("launch-1"));
			Assert.That(reloaded.PluginId, Is.EqualTo("com.example.plugin"));
			Assert.That(reloaded.ProcessId, Is.EqualTo(1234));
			Assert.That(reloaded.StartedAt, Is.EqualTo(startedAt).Within(TimeSpan.FromSeconds(1)));
		});
	}

	[Test]
	public void Two_launches_of_the_same_plugin_are_kept_apart_by_their_launch_ids()
	{
		var journal = CreateJournal();
		var startedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
		journal.Record(Entry("launch-1", "com.example.plugin", 1111, startedAt)).GetAwaiter().GetResult();
		journal.Record(Entry("launch-2", "com.example.plugin", 2222, startedAt)).GetAwaiter().GetResult();

		journal.Remove("launch-1").GetAwaiter().GetResult();

		var remaining = CreateJournal().Load().Entries.Single();
		Assert.Multiple(() =>
		{
			Assert.That(remaining.LaunchId, Is.EqualTo("launch-2"));
			Assert.That(remaining.ProcessId, Is.EqualTo(2222));
		});
	}

	[Test]
	public void The_owner_record_names_the_host_process_that_wrote_the_journal()
	{
		var journal = CreateJournal();
		journal.Record(Entry("launch-1", "com.example.plugin", 1234, DateTimeOffset.UtcNow))
			.GetAwaiter()
			.GetResult();

		using var current = Process.GetCurrentProcess();
		var owner = CreateJournal().Load().Owner;

		Assert.That(owner, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(owner!.ProcessId, Is.EqualTo(Environment.ProcessId));
			Assert.That(owner.StartedAt,
				Is.EqualTo(new DateTimeOffset(current.StartTime.ToUniversalTime()))
					.Within(TimeSpan.FromSeconds(1)));
		});
	}

	[Test]
	public void A_journal_that_was_never_written_loads_as_empty()
		=> Assert.That(CreateJournal().Load().Entries, Is.Empty);

	[Test]
	public void A_corrupt_journal_loads_as_empty_without_throwing()
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		File.WriteAllText(Path.Combine(_paths.ConfigDirectory, "plugin-processes.json"), "not json at all");

		PluginProcessJournalSnapshot snapshot = null!;
		Assert.DoesNotThrow(() => snapshot = CreateJournal().Load());
		Assert.That(snapshot.Entries, Is.Empty);
	}
}
