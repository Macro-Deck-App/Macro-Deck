using MacroDeckHost.Integrations.Voicemeeter;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterConnectionTests
{
	private FakeVoicemeeterRemote _remote = null!;
	private RecordingPublisher _publisher = null!;
	private VoicemeeterConnection _connection = null!;

	[SetUp]
	public void SetUp()
	{
		_remote = new FakeVoicemeeterRemote();
		_publisher = new RecordingPublisher();
		_connection = new VoicemeeterConnection(_remote, new VoicemeeterEventEmitter(_publisher));
	}

	[TearDown]
	public void TearDown()
	{
		_connection.Dispose();
		_remote.Dispose();
	}

	[Test]
	public void A_closed_Voicemeeter_reads_as_disconnected_without_an_event()
	{
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.IsConnected, Is.False);
			// Nothing "disconnected" - it was never connected, and a host that starts before
			// Voicemeeter must not fire a spurious trigger.
			Assert.That(_publisher.Published, Is.Empty);
		});
	}

	[Test]
	public void Connecting_builds_the_edition_layout_and_reports_it_once()
	{
		_remote.Run(VoicemeeterEdition.Banana);

		_connection.Poll();
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.IsConnected, Is.True);
			Assert.That(_connection.State.Edition, Is.EqualTo(VoicemeeterEdition.Banana));
			Assert.That(_connection.State.Strips, Has.Count.EqualTo(5));
			Assert.That(_connection.State.Buses, Has.Count.EqualTo(5));
			Assert.That(_connection.State.Version, Is.EqualTo("3.0.2.8"));
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.Connected }));
		});
	}

	[Test]
	public void A_strip_carries_its_label_solo_and_bus_sends()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_remote.UserSets(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Label), "Mic");
		_remote.UserSets(VoicemeeterParameters.StripBusAssignment(0, "A1"), 1f);
		_remote.UserSets(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Solo), 1f);

		_connection.Poll();

		var strip = _connection.State.Strips[0];
		Assert.Multiple(() =>
		{
			Assert.That(strip.Label, Is.EqualTo("Mic"));
			Assert.That(strip.Solo, Is.True);
			Assert.That(strip.Assignments["A1"], Is.True);
			Assert.That(strip.Assignments["B2"], Is.False);
			Assert.That(strip.Assignments, Has.Count.EqualTo(5));
		});
	}

	[Test]
	public void A_virtual_strips_solo_switch_is_never_asked_for()
	{
		_remote.Run(VoicemeeterEdition.Banana);

		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.Strips[4].Solo, Is.False);
			Assert.That(_remote.Reads, Does.Contain("Strip[2].Solo"));
			Assert.That(_remote.Reads, Does.Not.Contain("Strip[3].Solo"));
			Assert.That(_remote.Reads, Does.Not.Contain("Strip[4].Solo"));
		});
	}

	[Test]
	public void Nothing_is_re_read_while_the_parameters_are_clean()
	{
		_remote.Run(VoicemeeterEdition.Potato);
		_connection.Poll();
		var first = _connection.State;

		_connection.Poll();
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State, Is.SameAs(first));
			Assert.That(_publisher.Published, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_dirty_parameter_rebuilds_the_snapshot()
	{
		_remote.Run(VoicemeeterEdition.Standard);
		_connection.Poll();

		_remote.UserSets(VoicemeeterParameters.Strip(1, VoicemeeterParameters.Gain), -6.5f);
		_connection.Poll();

		Assert.That(_connection.State.Strips[1].Gain, Is.EqualTo(-6.5f).Within(0.001f));
	}

	[Test]
	public void Closing_Voicemeeter_disconnects_and_reports_it_once()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_connection.Poll();
		_publisher.Clear();

		_remote.Close();
		_connection.Poll();
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.IsConnected, Is.False);
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.Disconnected }));
		});
	}

	[Test]
	public void Closing_Voicemeeter_leaves_the_channel_catalogue_alone()
	{
		_remote.Run(VoicemeeterEdition.Potato);
		_remote.UserSets(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Label), "Mic");
		_connection.Poll();

		_remote.Close();
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.Catalog.Strips, Has.Count.EqualTo(8));
			Assert.That(_connection.Catalog.Strips[0].Label, Is.EqualTo("Mic"));
			Assert.That(_connection.Catalog.Edition, Is.EqualTo(VoicemeeterEdition.Potato));
		});
	}

	[Test]
	public void Reconnecting_does_not_replay_the_mix_as_change_events()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_connection.Poll();
		_remote.Close();
		_connection.Poll();
		_publisher.Clear();

		_remote.Run(VoicemeeterEdition.Banana);
		_remote.UserSets(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Mute), 1f);
		_remote.UserSets(VoicemeeterParameters.Bus(0, VoicemeeterParameters.Gain), -12f);
		_connection.Poll();

		Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.Connected }));
	}

	[Test]
	public void Switching_edition_is_treated_as_a_reconnect()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_connection.Poll();
		_publisher.Clear();

		_remote.Run(VoicemeeterEdition.Potato);
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.Strips, Has.Count.EqualTo(8));
			Assert.That(_publisher.Ids,
				Is.EqualTo(new[] { VoicemeeterEventIds.Disconnected, VoicemeeterEventIds.Connected }));
		});
	}

	[Test]
	public void A_macro_button_press_from_elsewhere_is_picked_up()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_connection.Poll();
		_publisher.Clear();

		_remote.UserPressesMacroButton(7, state: true);
		_connection.Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_connection.State.MacroButtons[7], Is.True);
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.MacroButtonChanged }));
			Assert.That(_publisher.Published[0].Parameters?["button"], Is.EqualTo(7));
		});
	}

	[Test]
	public void Start_logs_in_once_and_Dispose_logs_out()
	{
		_remote.Run(VoicemeeterEdition.Banana);

		_connection.Start();
		_connection.Dispose();

		Assert.Multiple(() =>
		{
			Assert.That(_remote.LoginCount, Is.EqualTo(1));
			Assert.That(_remote.LogoutCount, Is.EqualTo(1));
			Assert.That(_remote.IsDisposed, Is.True);
		});
	}

	[Test]
	public void An_unavailable_remote_is_never_logged_into()
	{
		var unavailable = new FakeVoicemeeterRemote { IsAvailable = false, UnavailableReason = "not Windows" };
		using var connection = new VoicemeeterConnection(unavailable);

		connection.Start();

		Assert.Multiple(() =>
		{
			Assert.That(unavailable.LoginCount, Is.Zero);
			Assert.That(connection.IsAvailable, Is.False);
			Assert.That(TestLocalization.Resolve(connection.UnavailableReason), Is.EqualTo("not Windows"));
		});
	}

	[Test]
	public void The_packed_version_is_read_one_byte_per_component()
		=> Assert.That(VoicemeeterConnection.FormatVersion((3 << 24) | (0 << 16) | (2 << 8) | 8),
			Is.EqualTo("3.0.2.8"));

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public IEnumerable<string> Ids => Published.Select(entry => entry.EventId);

		public void Clear() => Published.Clear();

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
