using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopStateMapperTests
{
	private static readonly Func<string, string> _registerArtwork = url => $"id:{url}";

	private static readonly string[] _oneArtist = ["Author"];

	[TestCase(YtmTrackState.Playing, PlaybackState.Playing)]
	[TestCase(YtmTrackState.Paused, PlaybackState.Paused)]
	[TestCase(YtmTrackState.Unknown, PlaybackState.Stopped)]
	public void A_track_state_maps_to_its_playback_state(YtmTrackState trackState, PlaybackState expected)
	{
		var snapshot = Map(Player(trackState), Video());

		Assert.That(snapshot.Player.PlaybackState, Is.EqualTo(expected));
	}

	[Test]
	public void Buffering_still_counts_as_playing()
	{
		var snapshot = Map(Player(YtmTrackState.Buffering), Video());

		Assert.That(snapshot.Player.PlaybackState, Is.EqualTo(PlaybackState.Playing));
	}

	[Test]
	public void Without_a_video_the_track_fields_are_empty_but_the_player_is_still_connected()
	{
		var snapshot = Map(Player(YtmTrackState.Playing, volume: 42), video: null);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.IsConnected, Is.True);
			Assert.That(snapshot.Player.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
			Assert.That(snapshot.Player.TrackName, Is.Null);
			Assert.That(snapshot.Player.Artists, Is.Empty);
			Assert.That(snapshot.Player.ArtworkId, Is.Null);
			Assert.That(snapshot.Player.Position, Is.Null);
			Assert.That(snapshot.Player.Duration, Is.Null);
			Assert.That(snapshot.Player.VolumePercent, Is.EqualTo(42));
		});
	}

	[Test]
	public void The_track_details_map_across()
	{
		var snapshot = Map(Player(YtmTrackState.Playing, progress: 30), Video(album: "Album"));

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.TrackName, Is.EqualTo("Title"));
			Assert.That(snapshot.Player.Artists, Is.EqualTo(_oneArtist));
			Assert.That(snapshot.Player.AlbumName, Is.EqualTo("Album"));
			Assert.That(snapshot.Player.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
			Assert.That(snapshot.Player.Duration, Is.EqualTo(TimeSpan.FromSeconds(180)));
			Assert.That(snapshot.VideoId, Is.EqualTo("abc"));
		});
	}

	[Test]
	public void A_blank_author_yields_no_artists()
	{
		var snapshot = Map(Player(YtmTrackState.Playing), Video(author: "   "));

		Assert.That(snapshot.Player.Artists, Is.Empty);
	}

	[Test]
	public void A_duration_of_zero_reads_as_unknown()
	{
		var snapshot = Map(Player(YtmTrackState.Playing), Video(durationSeconds: 0));

		Assert.That(snapshot.Player.Duration, Is.Null);
	}

	[Test]
	public void The_largest_thumbnail_wins_and_the_same_url_yields_the_same_id()
	{
		var thumbnails = new List<YtmThumbnail>
		{
			new("small", 60, 60), new("large", 544, 544), new("medium", 120, 120)
		};

		var first = Map(Player(YtmTrackState.Playing), Video(thumbnails: thumbnails));
		var second = Map(Player(YtmTrackState.Playing), Video(thumbnails: thumbnails));

		Assert.Multiple(() =>
		{
			Assert.That(first.Player.ArtworkId, Is.EqualTo("id:large"));
			Assert.That(second.Player.ArtworkId, Is.EqualTo(first.Player.ArtworkId));
		});
	}

	[Test]
	public void Without_thumbnails_there_is_no_artwork_id()
	{
		var snapshot = Map(Player(YtmTrackState.Playing), Video(thumbnails: []));

		Assert.That(snapshot.Player.ArtworkId, Is.Null);
	}

	[Test]
	public void Muting_leaves_the_real_volume_in_place()
	{
		var snapshot = Map(Player(YtmTrackState.Playing, volume: 73, muted: true), Video());

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.VolumePercent, Is.EqualTo(73));
			Assert.That(snapshot.Muted, Is.True);
		});
	}

	[Test]
	public void A_volume_outside_the_range_is_clamped()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Map(Player(YtmTrackState.Playing, volume: 140), Video()).Player.VolumePercent, Is.EqualTo(100));
			Assert.That(Map(Player(YtmTrackState.Playing, volume: -5), Video()).Player.VolumePercent, Is.EqualTo(0));
		});
	}

	[TestCase(YtmRepeatMode.None, RepeatMode.Off)]
	[TestCase(YtmRepeatMode.All, RepeatMode.Context)]
	[TestCase(YtmRepeatMode.One, RepeatMode.Track)]
	[TestCase(YtmRepeatMode.Unknown, RepeatMode.Off)]
	public void A_repeat_mode_maps_inbound(YtmRepeatMode wire, RepeatMode expected)
	{
		var queue = new YtmQueueInfo(wire, 0, []);
		var snapshot = Map(Player(YtmTrackState.Playing, queue: queue), Video());

		Assert.That(snapshot.Player.RepeatMode, Is.EqualTo(expected));
	}

	[Test]
	public void Without_a_queue_the_repeat_mode_is_off_and_unknown()
	{
		var snapshot = Map(Player(YtmTrackState.Playing), Video());

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.RepeatMode, Is.EqualTo(RepeatMode.Off));
			Assert.That(snapshot.RepeatMode, Is.Null);
		});
	}

	[TestCase(RepeatMode.Off, 0)]
	[TestCase(RepeatMode.Context, 1)]
	[TestCase(RepeatMode.Track, 2)]
	public void A_repeat_mode_maps_outbound(RepeatMode mode, int expected)
	{
		Assert.That(YtmDesktopStateMapper.ToCompanionRepeatMode(mode), Is.EqualTo(expected));
	}

	[Test]
	public void The_device_is_a_constant_and_has_no_type()
	{
		var snapshot = Map(Player(YtmTrackState.Playing), Video());

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.DeviceName, Is.EqualTo("YouTube Music Desktop App"));
			Assert.That(snapshot.Player.DeviceType, Is.Null);
		});
	}

	[Test]
	public void The_shuffle_belief_is_what_gets_reported()
	{
		var snapshot = YtmDesktopStateMapper.Map(new YtmPlayerState(Player(YtmTrackState.Playing), Video()),
			shuffleBelief: true,
			_registerArtwork);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Player.ShuffleEnabled, Is.True);
			Assert.That(snapshot.ShuffleEnabled, Is.True);
		});
	}

	[Test]
	public void Fields_a_server_did_not_report_stay_null()
	{
		var video = new YtmVideoInfo("abc",
			"Title",
			"Author",
			null,
			null,
			null,
			LikeStatus: null,
			[],
			180,
			IsLive: null,
			VideoType: null,
			MetadataFilled: null);

		var snapshot = Map(Player(YtmTrackState.Playing, muted: null), video);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Muted, Is.Null);
			Assert.That(snapshot.IsLive, Is.Null);
			Assert.That(snapshot.VideoType, Is.Null);
			Assert.That(snapshot.LikeStatus, Is.Null);
		});
	}

	[Test]
	public void An_unknown_rating_or_media_type_reads_as_null()
	{
		var snapshot = Map(Player(YtmTrackState.Playing),
			Video(likeStatus: YtmLikeStatus.Unknown, videoType: YtmVideoType.Unknown));

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.LikeStatus, Is.Null);
			Assert.That(snapshot.VideoType, Is.Null);
		});
	}

	[Test]
	public void The_queue_comes_across_for_the_track_catalog()
	{
		var queue = new YtmQueueInfo(YtmRepeatMode.None, 0, [new YtmQueueItem("v1", "One", "A", "3:00", true, [])]);
		var snapshot = Map(Player(YtmTrackState.Playing, queue: queue), Video());

		Assert.That(snapshot.Queue, Has.Count.EqualTo(1));
	}

	private static YtmDesktopSnapshot Map(YtmPlayerInfo player, YtmVideoInfo? video)
		=> YtmDesktopStateMapper.Map(new YtmPlayerState(player, video),
			shuffleBelief: false,
			_registerArtwork);

	private static YtmPlayerInfo Player(
		YtmTrackState trackState,
		double progress = 0,
		int volume = 50,
		bool? muted = false,
		YtmQueueInfo? queue = null)
		=> new(trackState, progress, volume, muted, AdPlaying: false, queue);

	private static YtmVideoInfo Video(
		string? author = "Author",
		string? album = null,
		int durationSeconds = 180,
		IReadOnlyList<YtmThumbnail>? thumbnails = null,
		YtmLikeStatus? likeStatus = YtmLikeStatus.Indifferent,
		YtmVideoType? videoType = YtmVideoType.Audio)
		=> new("abc",
			"Title",
			author,
			"channel",
			album,
			null,
			likeStatus,
			thumbnails ?? [],
			durationSeconds,
			IsLive: false,
			videoType,
			MetadataFilled: true);
}
