using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The resolver is where the migration's efficiency requirement actually lands: a timeline the reader is
/// already extrapolating correctly must not be re-anchored, because that is the difference between a
/// playing track costing one small patch every two seconds and costing nothing at all.
/// </summary>
[TestFixture]
public class MusicPlayerViewStateResolverTests
{
	[Test]
	public async Task A_playing_track_anchors_its_position_and_leaves_the_rate_unstated()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 42_000));

		var state = await harness.ResolveAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.Position!.PositionMs, Is.EqualTo(42_000));
			Assert.That(state.Position.DurationMs, Is.EqualTo(215_000));
			Assert.That(state.Position.Anchor, Is.EqualTo(harness.Now));
			// Absent means normal speed, which is what keeps the common case key-free on the wire.
			Assert.That(state.Position.Rate, Is.Null);
		});
	}

	[Test]
	public async Task A_position_the_reader_is_already_extrapolating_correctly_is_left_exactly_as_it_was()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 42_000));
		var first = await harness.ResolveAsync();

		// Two seconds later the provider reports two seconds further along - precisely what the reference
		// already predicts, so re-anchoring would spend a patch to change nothing.
		harness.Advance(TimeSpan.FromSeconds(2));
		harness.Record(Playing(positionMs: 44_000));
		var second = await harness.ResolveAsync(first);

		Assert.That(second.Position, Is.SameAs(first.Position));
	}

	[Test]
	public async Task A_position_that_has_drifted_past_the_tolerance_is_re_anchored()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 42_000));
		var first = await harness.ResolveAsync();

		// A seek: two seconds passed, but playback jumped a minute.
		harness.Advance(TimeSpan.FromSeconds(2));
		harness.Record(Playing(positionMs: 104_000));
		var second = await harness.ResolveAsync(first);

		Assert.Multiple(() =>
		{
			Assert.That(second.Position, Is.Not.SameAs(first.Position));
			Assert.That(second.Position!.PositionMs, Is.EqualTo(104_000));
			Assert.That(second.Position.Anchor, Is.EqualTo(harness.Now));
		});
	}

	[Test]
	public async Task Pausing_re_anchors_even_where_the_position_itself_did_not_move()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 42_000));
		var first = await harness.ResolveAsync();

		harness.Record(Playing(positionMs: 42_000, isPlaying: false));
		var second = await harness.ResolveAsync(first);

		Assert.Multiple(() =>
		{
			// Rate is the whole difference: an unchanged reference would keep counting up while paused.
			Assert.That(second.Position!.Rate, Is.Zero);
			Assert.That(second.IsPaused, Is.True);
		});
	}

	[Test]
	public async Task A_track_change_re_anchors_even_where_the_new_position_matches_the_prediction()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 42_000));
		var first = await harness.ResolveAsync();

		harness.Advance(TimeSpan.FromSeconds(2));
		harness.Record(Playing(positionMs: 44_000, trackName: "Nannou"));
		var second = await harness.ResolveAsync(first);

		Assert.That(second.Position, Is.Not.SameAs(first.Position));
	}

	[Test]
	public async Task A_player_with_no_length_still_carries_a_position_to_advance()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 3_600_000, durationMs: null));

		var state = await harness.ResolveAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.Position, Is.Not.Null);
			// Absent, never zero: a live stream has a position and no end, and a bar must not divide by it.
			Assert.That(state.Position!.DurationMs, Is.Null);
		});
	}

	[Test]
	public async Task A_player_that_has_not_answered_yet_is_loading_rather_than_disconnected()
	{
		var harness = new MusicPlayerTestHarness();

		var state = await harness.ResolveAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsLoading, Is.True);
			Assert.That(state.IsConnected, Is.False);
			Assert.That(state.Position, Is.Null);
		});
	}

	[Test]
	public async Task Cover_art_travels_as_a_resource_and_is_fetched_once_per_track()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 1_000));

		var first = await harness.ResolveAsync();
		harness.Advance(TimeSpan.FromSeconds(2));
		harness.Record(Playing(positionMs: 3_000));
		var second = await harness.ResolveAsync(first);

		Assert.Multiple(() =>
		{
			Assert.That(first.Artwork, Is.Not.Null);
			Assert.That(first.Accent, Is.EqualTo("#aabbcc"));
			Assert.That(first.Background, Is.EqualTo("#112233"));
			// The same cover must not be refetched and re-analysed on every poll.
			Assert.That(harness.ArtworkFetches, Is.EqualTo(1));
			Assert.That(second.Artwork, Is.EqualTo(first.Artwork));
		});
	}

	[Test]
	public async Task A_track_with_no_cover_drops_the_colours_with_it()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 1_000));
		var first = await harness.ResolveAsync();

		harness.Record(Playing(positionMs: 1_000, artworkId: null));
		var second = await harness.ResolveAsync(first);

		Assert.Multiple(() =>
		{
			Assert.That(second.Artwork, Is.Null);
			Assert.That(second.Accent, Is.Null);
			Assert.That(second.Background, Is.Null);
		});
	}

	[Test]
	public async Task A_selection_that_no_longer_exists_falls_back_and_says_so()
	{
		var harness = new MusicPlayerTestHarness();
		harness.Record(Playing(positionMs: 1_000));

		var state = await harness.ResolveAsync(config: new MusicPlayerWidgetData { InstanceId = "gone" });

		Assert.Multiple(() =>
		{
			// Falling back silently would be indistinguishable from a broken connection.
			Assert.That(state.InstanceMissing, Is.True);
			Assert.That(state.TrackName, Is.EqualTo("Windowlicker"));
		});
	}

	private static MusicPlayerStatePayload Playing(
		long positionMs,
		string trackName = "Windowlicker",
		string? artworkId = "cover-1",
		long? durationMs = 215_000,
		bool isPlaying = true)
		=> new()
		{
			InstanceId = MusicPlayerTestHarness.InstanceId,
			IsConnected = true,
			IsPlaying = isPlaying,
			PlaybackState = isPlaying ? "playing" : "paused",
			TrackName = trackName,
			ArtistName = "Aphex Twin",
			AlbumName = "Windowlicker",
			ArtworkId = artworkId,
			PositionMs = positionMs,
			DurationMs = durationMs,
		};
}
