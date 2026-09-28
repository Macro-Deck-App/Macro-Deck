using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class VideoStreamSessionTableTests
{
	private static readonly TimeSpan _tombstoneLifetime = TimeSpan.FromSeconds(30);

	private readonly object _instance = new();
	private ManualTimeProvider _time = null!;
	private VideoStreamSessionTable<VideoStreamSessionReason, object> _table = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_table = new VideoStreamSessionTable<VideoStreamSessionReason, object>(_time, _tombstoneLifetime, maxTombstones: 4);
	}

	[Test]
	public void A_session_is_opening_until_its_open_completes_and_then_open()
	{
		Assert.That(_table.TryBeginOpen("s1", "cam", _instance, 0), Is.EqualTo(VideoStreamOpenAdmission.Admitted));
		_table.TryGet("s1", out var opening);

		var completion = _table.CompleteOpen("s1");
		_table.TryGet("s1", out var open);

		Assert.Multiple(() =>
		{
			Assert.That(opening.Phase, Is.EqualTo(VideoStreamSessionPhase.Opening));
			Assert.That(completion.Outcome, Is.EqualTo(VideoStreamOpenOutcome.Open));
			Assert.That(open.Phase, Is.EqualTo(VideoStreamSessionPhase.Open));
			Assert.That(open.ProviderId, Is.EqualTo("cam"));
		});
	}

	[Test]
	public void Closing_an_open_session_asks_for_exactly_one_close()
	{
		Open("s1");

		var first = _table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed);
		var second = _table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed);

		Assert.Multiple(() =>
		{
			Assert.That(first.Outcome, Is.EqualTo(VideoStreamCloseOutcome.CloseNow));
			Assert.That(first.Close!.Reason, Is.EqualTo(VideoStreamSessionReason.ConsumerClosed));
			Assert.That(second.Outcome, Is.EqualTo(VideoStreamCloseOutcome.AlreadyClosed));
			Assert.That(_table.TryGet("s1", out _), Is.False);
		});
	}

	[Test]
	public void A_close_during_the_open_is_carried_out_once_when_the_open_returns()
	{
		_table.TryBeginOpen("s1", "cam", _instance, 0);

		var request = _table.RequestClose("s1", VideoStreamSessionReason.ConsumerDisconnected);
		var repeated = _table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed);
		_table.TryGet("s1", out var closing);
		var completion = _table.CompleteOpen("s1");
		var late = _table.CompleteOpen("s1");

		Assert.Multiple(() =>
		{
			Assert.That(request.Outcome, Is.EqualTo(VideoStreamCloseOutcome.Deferred));
			Assert.That(repeated.Outcome, Is.EqualTo(VideoStreamCloseOutcome.AlreadyClosed));
			Assert.That(closing.Phase, Is.EqualTo(VideoStreamSessionPhase.Closed));
			Assert.That(completion.Outcome, Is.EqualTo(VideoStreamOpenOutcome.CloseNow));
			Assert.That(completion.Close!.Reason, Is.EqualTo(VideoStreamSessionReason.ConsumerDisconnected));
			Assert.That(late.Outcome, Is.EqualTo(VideoStreamOpenOutcome.Discard));
		});
	}

	[Test]
	public void A_failed_open_with_a_pending_close_needs_no_close()
	{
		_table.TryBeginOpen("s1", "cam", _instance, 0);
		_table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed);

		_table.FailOpen("s1");

		Assert.Multiple(() =>
		{
			Assert.That(_table.CompleteOpen("s1").Outcome, Is.EqualTo(VideoStreamOpenOutcome.Discard));
			Assert.That(_table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed).Outcome,
				Is.EqualTo(VideoStreamCloseOutcome.AlreadyClosed));
		});
	}

	[Test]
	public void A_session_the_provider_closed_itself_is_never_closed_again()
	{
		Open("open");
		_table.TryBeginOpen("opening", "cam", _instance, 0);

		var endedOpen = _table.EndByProvider("open", out var provider);
		var endedOpening = _table.EndByProvider("opening", out _);
		var hostClose = _table.RequestClose("open", VideoStreamSessionReason.ConsumerClosed);
		var completion = _table.CompleteOpen("opening");

		Assert.Multiple(() =>
		{
			Assert.That(endedOpen, Is.True);
			Assert.That(provider, Is.EqualTo("cam"));
			Assert.That(endedOpening, Is.True);
			Assert.That(hostClose.Outcome, Is.EqualTo(VideoStreamCloseOutcome.AlreadyClosed));
			Assert.That(completion.Outcome, Is.EqualTo(VideoStreamOpenOutcome.Discard));
		});
	}

	[Test]
	public void A_close_that_overtakes_its_open_refuses_the_open_until_the_tombstone_expires()
	{
		var close = _table.RequestClose("s1", VideoStreamSessionReason.ConsumerClosed);
		var refused = _table.TryBeginOpen("s1", "cam", _instance, 0);

		_time.Advance(_tombstoneLifetime);
		var admitted = _table.TryBeginOpen("s1", "cam", _instance, 0);

		Assert.Multiple(() =>
		{
			Assert.That(close.Outcome, Is.EqualTo(VideoStreamCloseOutcome.AlreadyClosed));
			Assert.That(refused, Is.EqualTo(VideoStreamOpenAdmission.Tombstoned));
			Assert.That(admitted, Is.EqualTo(VideoStreamOpenAdmission.Admitted));
		});
	}

	[Test]
	public void Tombstones_are_bounded_and_the_oldest_goes_first()
	{
		foreach (var id in new[] { "a", "b", "c", "d", "e" })
		{
			_table.RequestClose(id, VideoStreamSessionReason.ConsumerClosed);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_table.TryBeginOpen("a", "cam", _instance, 0), Is.EqualTo(VideoStreamOpenAdmission.Admitted));
			Assert.That(_table.TryBeginOpen("e", "cam", _instance, 0), Is.EqualTo(VideoStreamOpenAdmission.Tombstoned));
		});
	}

	[Test]
	public void A_second_open_with_a_live_session_id_is_a_duplicate()
	{
		_table.TryBeginOpen("s1", "cam", _instance, 0);

		Assert.That(_table.TryBeginOpen("s1", "cam", _instance, 0), Is.EqualTo(VideoStreamOpenAdmission.Duplicate));
	}

	[Test]
	public void Sessions_of_an_older_connection_epoch_are_closed_and_newer_ones_kept()
	{
		var before = _table.CurrentEpoch;
		Open("old", before);
		_table.TryBeginOpen("old-opening", "cam", _instance, before);
		var current = _table.AdvanceEpoch();
		Open("new", current);

		var closing = _table.CloseOlderThan(current, VideoStreamSessionReason.HostDisconnected);
		var lateOpen = _table.CompleteOpen("old-opening");

		Assert.Multiple(() =>
		{
			Assert.That(current, Is.GreaterThan(before));
			Assert.That(closing.Select(close => close.SessionId), Is.EqualTo(new[] { "old" }));
			Assert.That(closing[0].Reason, Is.EqualTo(VideoStreamSessionReason.HostDisconnected));
			Assert.That(lateOpen.Outcome, Is.EqualTo(VideoStreamOpenOutcome.CloseNow));
			Assert.That(lateOpen.Close!.Reason, Is.EqualTo(VideoStreamSessionReason.HostDisconnected));
			Assert.That(_table.TryGet("new", out var kept) && kept.Phase == VideoStreamSessionPhase.Open, Is.True);
		});
	}

	[Test]
	public void Closing_a_provider_leaves_other_providers_alone()
	{
		var other = new object();
		Open("a", provider: "cam");
		Open("b", provider: "obs", instance: other);

		var closing = _table.CloseProvider(_instance, VideoStreamSessionReason.ProviderRemoved);

		Assert.Multiple(() =>
		{
			Assert.That(closing.Select(close => close.SessionId), Is.EqualTo(new[] { "a" }));
			Assert.That(_table.TryGet("b", out _), Is.True);
		});
	}

	[Test]
	public void Closing_a_provider_spares_sessions_of_a_replacement_registered_under_the_same_id()
	{
		var replacement = new object();
		Open("old", provider: "cam");
		Open("new", provider: "cam", instance: replacement);

		var closing = _table.CloseProvider(_instance, VideoStreamSessionReason.ProviderRemoved);

		Assert.Multiple(() =>
		{
			Assert.That(closing.Select(close => close.SessionId), Is.EqualTo(new[] { "old" }));
			Assert.That(_table.TryGet("new", out var kept) && kept.Phase == VideoStreamSessionPhase.Open, Is.True);
		});
	}

	[Test]
	public void Closing_everything_twice_closes_each_session_once()
	{
		Open("a");
		Open("b");

		var first = _table.CloseAll(VideoStreamSessionReason.ProviderRemoved);
		var second = _table.CloseAll(VideoStreamSessionReason.ProviderRemoved);

		Assert.Multiple(() =>
		{
			Assert.That(first.Select(close => close.SessionId), Is.EquivalentTo(new[] { "a", "b" }));
			Assert.That(second, Is.Empty);
		});
	}

	private void Open(string sessionId, long epoch = 0, string provider = "cam", object? instance = null)
	{
		_table.TryBeginOpen(sessionId, provider, instance ?? _instance, epoch);
		_table.CompleteOpen(sessionId);
	}
}
