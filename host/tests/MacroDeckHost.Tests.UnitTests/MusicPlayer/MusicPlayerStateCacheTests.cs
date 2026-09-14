using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerStateCacheTests
{
	private const string A = "spotify::default";
	private const string B = "sinusbot::b";

	[Test]
	public void A_player_that_starts_playing_becomes_active()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Paused("Two"));

		cache.Record(B, Playing("Two"));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(B));
	}

	[Test]
	public void A_player_already_playing_on_its_first_read_becomes_active_only_while_none_is()
	{
		var cache = new MusicPlayerStateCache();

		cache.Record(A, Playing("One"));
		cache.Record(B, Playing("Two"));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void A_player_already_playing_on_its_first_read_takes_over_from_a_paused_one()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(A, Paused("One"));

		cache.Record(B, Playing("Two"));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(B));
	}

	[Test]
	public void A_track_change_while_playing_takes_over()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Playing("Two"));

		cache.Record(B, Playing("Three"));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(B));
	}

	[Test]
	public void A_track_change_to_an_empty_name_does_not_take_over()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Playing("Two"));

		cache.Record(B, Playing(null));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void Pausing_the_active_player_keeps_it_active()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Paused("Two"));

		cache.Record(A, Paused("One"));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void A_position_tick_on_another_playing_player_does_not_take_over()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Paused("One"));
		cache.Record(B, Playing("Two", positionMs: 1_000));
		cache.Record(A, Playing("One"));

		cache.Record(B, Playing("Two", positionMs: 3_000));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void The_active_player_disconnecting_releases_focus()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));

		cache.Record(A, MusicPlayerStatePayload.Disconnected(A));

		Assert.That(cache.ActiveInstanceId, Is.Null);
	}

	[Test]
	public void The_active_player_disconnecting_hands_focus_to_one_still_playing()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Playing("Two"));
		cache.Record(B, Playing("Three"));

		cache.Record(B, MusicPlayerStatePayload.Disconnected(B));

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void Forgetting_the_active_player_hands_focus_to_one_still_playing()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Playing("Two"));
		cache.Record(B, Playing("Three"));

		cache.Forget(new HashSet<string>(StringComparer.Ordinal) { A });

		Assert.That(cache.ActiveInstanceId, Is.EqualTo(A));
	}

	[Test]
	public void Forgetting_the_active_player_releases_focus()
	{
		var cache = new MusicPlayerStateCache();
		cache.Record(A, Playing("One"));
		cache.Record(B, Paused("Two"));

		cache.Forget(new HashSet<string>(StringComparer.Ordinal) { B });

		Assert.That(cache.ActiveInstanceId, Is.Null);
	}

	private static MusicPlayerStatePayload Playing(string? track, long positionMs = 0)
		=> new()
		{
			IsConnected = true,
			IsPlaying = true,
			PlaybackState = "playing",
			TrackName = track,
			PositionMs = positionMs,
		};

	private static MusicPlayerStatePayload Paused(string track)
		=> new()
		{
			IsConnected = true,
			IsPlaying = false,
			PlaybackState = "paused",
			TrackName = track,
		};
}
