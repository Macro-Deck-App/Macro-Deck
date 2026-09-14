using MacroDeck.Sdk.Decks;
using MacroDeckHost.Application.Deck;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class DeckClientTrackerTests
{
	private DeckClientTracker _tracker = null!;
	private List<DeckClientChangedEventArgs> _changes = null!;
	private int _stateChanges;

	[SetUp]
	public void SetUp()
	{
		_tracker = new DeckClientTracker(Serilog.Core.Logger.None);
		_changes = [];
		_stateChanges = 0;
		_tracker.ClientChanged += (_, change) => _changes.Add(change);
		_tracker.StateChanged += () => _stateChanges++;
	}

	[Test]
	public void A_client_seen_for_the_first_time_is_listed_and_reported_with_no_previous_position()
	{
		Report("tab-1", null, "p1", "f1");

		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Snapshot().Single().FolderId, Is.EqualTo("f1"));
			Assert.That(_changes.Single().Client.ClientId, Is.EqualTo("tab-1"));
			Assert.That(_changes.Single().PreviousFolderId, Is.Null);
			Assert.That(_changes.Single().PreviousProfileId, Is.Null);
			Assert.That(_stateChanges, Is.EqualTo(1));
		});
	}

	[Test]
	public void Reporting_the_same_folder_again_changes_nothing()
	{
		Report("tab-1", null, "p1", "f1");

		Report("tab-1", null, "p1", "f1");

		Assert.Multiple(() =>
		{
			Assert.That(_changes, Has.Count.EqualTo(1));
			Assert.That(_stateChanges, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_move_is_reported_with_the_previous_profile_and_folder()
	{
		Report("tab-1", null, "p1", "f1");

		Report("tab-1", null, "p2", "f2");

		var move = _changes.Last();
		Assert.Multiple(() =>
		{
			Assert.That(move.Client.ProfileId, Is.EqualTo("p2"));
			Assert.That(move.Client.FolderId, Is.EqualTo("f2"));
			Assert.That(move.PreviousProfileId, Is.EqualTo("p1"));
			Assert.That(move.PreviousFolderId, Is.EqualTo("f1"));
		});
	}

	[Test]
	public void Two_clients_on_different_folders_are_listed_independently()
	{
		Report("tab-1", null, "p1", "f1");
		Report("tab-2", null, "p1", "f2");

		Assert.That(_tracker.Snapshot().Select(client => (client.ClientId, client.FolderId)),
			Is.EquivalentTo(new[] { ("tab-1", "f1"), ("tab-2", "f2") }));
	}

	[Test]
	public void Removing_a_client_drops_it_and_raises_only_the_state_change()
	{
		Report("tab-1", null, "p1", "f1");

		_tracker.Publish(_tracker.Remove("tab-1"));

		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Snapshot(), Is.Empty);
			Assert.That(_changes, Has.Count.EqualTo(1));
			Assert.That(_stateChanges, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_device_reporting_under_a_fresh_client_id_replaces_its_old_entry()
	{
		var device = Guid.NewGuid();
		Report("launch-1", device, "p1", "f1");

		Report("launch-2", device, "p1", "f1");

		Assert.That(_tracker.Snapshot().Select(client => client.ClientId), Is.EqualTo(new[] { "launch-2" }));
	}

	[Test]
	public void Removing_a_device_drops_every_entry_of_that_device_only()
	{
		var device = Guid.NewGuid();
		Report("device-client", device, "p1", "f1");
		Report("tab-1", null, "p1", "f1");

		_tracker.Publish(_tracker.RemoveDevice(device));

		Assert.That(_tracker.Snapshot().Select(client => client.ClientId), Is.EqualTo(new[] { "tab-1" }));
	}

	[Test]
	public void A_throwing_subscriber_does_not_stop_the_update_or_the_other_subscribers()
	{
		var tracker = new DeckClientTracker(Serilog.Core.Logger.None);
		var seen = 0;
		var stateSeen = 0;
		tracker.ClientChanged += (_, _) => throw new InvalidOperationException("plugin bug");
		tracker.ClientChanged += (_, _) => seen++;
		tracker.StateChanged += () => throw new InvalidOperationException("plugin bug");
		tracker.StateChanged += () => stateSeen++;

		Assert.DoesNotThrow(() => tracker.Publish(tracker.Report("tab-1", null, "p1", "f1")));
		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot(), Has.Count.EqualTo(1));
			Assert.That(seen, Is.EqualTo(1));
			Assert.That(stateSeen, Is.EqualTo(1));
		});
	}

	private void Report(string clientId, Guid? deviceId, string profileId, string folderId)
		=> _tracker.Publish(_tracker.Report(clientId, deviceId, profileId, folderId));
}
