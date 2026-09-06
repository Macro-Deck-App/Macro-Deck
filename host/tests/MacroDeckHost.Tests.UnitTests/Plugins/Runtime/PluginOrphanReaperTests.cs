using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginOrphanReaperTests
{
	private static readonly DateTimeOffset _hostStartedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private FakePluginProcessJournal _journal = null!;
	private FakeProcessTable _processTable = null!;

	[SetUp]
	public void SetUp()
	{
		_journal = new FakePluginProcessJournal();
		_processTable = new FakeProcessTable();
	}

	private PluginOrphanReaper CreateReaper()
		=> new(_journal, _processTable, Serilog.Core.Logger.None);

	private void PreviousSessionOwner()
		=> _journal.Owner = new PluginProcessJournalOwner { ProcessId = 999_001, StartedAt = _hostStartedAt };

	private void Journalled(string launchId, int processId, DateTimeOffset startedAt)
		=> _journal.Entries.Add(new PluginProcessJournalEntry
		{
			LaunchId = launchId,
			PluginId = "com.example." + launchId,
			ProcessId = processId,
			StartedAt = startedAt
		});

	[Test]
	public async Task A_journalled_process_left_by_a_dead_host_is_killed_and_dropped()
	{
		PreviousSessionOwner();
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.EqualTo(new[] { 2000 }));
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_reused_pid_is_dropped_without_being_killed()
	{
		PreviousSessionOwner();
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt.AddHours(1));

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.Empty);
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_journalled_pid_that_no_longer_exists_is_dropped_without_a_kill()
	{
		PreviousSessionOwner();
		Journalled("one", 2000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.Empty);
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task Plugins_owned_by_this_very_host_process_are_left_alone()
	{
		_journal.Owner = new PluginProcessJournalOwner
		{
			ProcessId = Environment.ProcessId,
			StartedAt = _hostStartedAt
		};
		_processTable.Seed(Environment.ProcessId, _hostStartedAt);
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.Empty);
			Assert.That(_journal.Entries, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task An_owner_pid_that_has_since_been_reused_does_not_protect_its_plugins()
	{
		_journal.Owner = new PluginProcessJournalOwner
		{
			ProcessId = Environment.ProcessId,
			StartedAt = _hostStartedAt.AddHours(-1)
		};
		_processTable.Seed(Environment.ProcessId, _hostStartedAt);
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.EqualTo(new[] { 2000 }));
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task Another_live_host_on_the_same_config_directory_keeps_its_plugins_and_its_journal()
	{
		_journal.Owner = new PluginProcessJournalOwner { ProcessId = 999_002, StartedAt = _hostStartedAt };
		_processTable.Seed(999_002, _hostStartedAt);
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.Empty);
			Assert.That(_journal.Entries.Select(entry => entry.LaunchId), Is.EqualTo(new[] { "one" }));
		});
	}

	[Test]
	public async Task Live_processes_that_were_never_journalled_are_never_touched()
	{
		PreviousSessionOwner();
		Journalled("one", 2000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);
		_processTable.Seed(3000, _hostStartedAt);
		_processTable.Seed(4000, _hostStartedAt);

		await CreateReaper().ReapAsync();

		Assert.That(_processTable.KillTreeCalls, Is.EqualTo(new[] { 2000 }));
	}

	[Test]
	public async Task One_kill_that_throws_does_not_abandon_the_remaining_entries()
	{
		PreviousSessionOwner();
		Journalled("one", 2000, _hostStartedAt);
		Journalled("two", 3000, _hostStartedAt);
		Journalled("three", 4000, _hostStartedAt);
		_processTable.Seed(2000, _hostStartedAt);
		_processTable.Seed(3000, _hostStartedAt);
		_processTable.Seed(4000, _hostStartedAt);
		_processTable.KillTreeThrowsFor.Add(3000);

		await CreateReaper().ReapAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_processTable.KillTreeCalls, Is.EqualTo(new[] { 2000, 4000 }));
			Assert.That(_journal.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_journal_kills_nothing()
	{
		PreviousSessionOwner();

		await CreateReaper().ReapAsync();

		Assert.That(_processTable.KillTreeCalls, Is.Empty);
	}
}
