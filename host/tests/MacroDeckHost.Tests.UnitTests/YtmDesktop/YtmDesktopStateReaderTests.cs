using System.Text.Json;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopStateReaderTests
{
	private const string FullModernPayloadJson =
		"""
		{
		  "player": {
		    "trackState": 1,
		    "videoProgress": 42.5,
		    "volume": 80,
		    "muted": false,
		    "adPlaying": false,
		    "queue": {
		      "autoplay": true,
		      "isGenerating": false,
		      "isInfinite": false,
		      "items": [
		        {
		          "thumbnails": [{ "url": "https://example.test/t1.jpg", "width": 120, "height": 90 }],
		          "title": "Track One",
		          "author": "Artist One",
		          "duration": "3:45",
		          "selected": true,
		          "videoId": "video-1",
		          "counterparts": null
		        }
		      ],
		      "automixItems": [],
		      "repeatMode": 1,
		      "selectedItemIndex": 0
		    }
		  },
		  "video": {
		    "author": "Artist One",
		    "channelId": "channel-1",
		    "title": "Track One",
		    "album": "Album One",
		    "albumId": "album-1",
		    "likeStatus": 2,
		    "thumbnails": [
		      { "url": "https://example.test/small.jpg", "width": 120, "height": 90 },
		      { "url": "https://example.test/large.jpg", "width": 640, "height": 480 }
		    ],
		    "durationSeconds": 225,
		    "id": "video-1",
		    "isLive": false,
		    "videoType": 0,
		    "metadataFilled": true
		  },
		  "playlistId": "playlist-1"
		}
		""";

	private const string PreTwoZeroSixPayloadJson =
		"""
		{
		  "player": {
		    "trackState": 1,
		    "videoProgress": 10,
		    "volume": 50,
		    "adPlaying": false
		  },
		  "video": {
		    "author": "Old Artist",
		    "channelId": "channel-2",
		    "title": "Old Track",
		    "album": null,
		    "albumId": null,
		    "thumbnails": [],
		    "durationSeconds": 100,
		    "id": "video-2"
		  }
		}
		""";

	private const string NullVideoAndQueueJson =
		"""
		{
		  "player": { "trackState": 0, "videoProgress": 0, "volume": 0, "adPlaying": false, "queue": null },
		  "video": null
		}
		""";

	private const string UnknownExtraPropertiesJson =
		"""
		{
		  "player": { "trackState": 1, "videoProgress": 1, "volume": 1, "adPlaying": false, "somethingNew": 123 },
		  "video": { "id": "v", "title": "t", "durationSeconds": 1, "somethingElse": "ignored" },
		  "extraTopLevel": true
		}
		""";

	private const string WrongTypesJson =
		"""
		{ "player": { "trackState": 1, "videoProgress": "not-a-number", "volume": "loud", "adPlaying": false } }
		""";

	private const string OutOfRangeEnumsJson =
		"""
		{
		  "player": {
		    "trackState": 99,
		    "videoProgress": 0,
		    "volume": 0,
		    "adPlaying": false,
		    "queue": { "repeatMode": 55, "selectedItemIndex": 0, "items": [] }
		  },
		  "video": { "id": "v", "title": "t", "durationSeconds": 1, "likeStatus": 42, "videoType": 77 }
		}
		""";

	[Test]
	public void Read_maps_every_field_of_a_full_modern_payload()
	{
		var state = YtmDesktopStateReader.Read(Parse(FullModernPayloadJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.TrackState, Is.EqualTo(YtmTrackState.Playing));
			Assert.That(state.Player.VideoProgress, Is.EqualTo(42.5));
			Assert.That(state.Player.Volume, Is.EqualTo(80));
			Assert.That(state.Player.Muted, Is.False);
			Assert.That(state.Player.AdPlaying, Is.False);
		});

		Assert.That(state.Player.Queue, Is.Not.Null);
		var queue = state.Player.Queue!;

		Assert.Multiple(() =>
		{
			Assert.That(queue.RepeatMode, Is.EqualTo(YtmRepeatMode.All));
			Assert.That(queue.SelectedItemIndex, Is.EqualTo(0));
			Assert.That(queue.Items, Has.Count.EqualTo(1));
		});

		var item = queue.Items[0];
		Assert.Multiple(() =>
		{
			Assert.That(item.VideoId, Is.EqualTo("video-1"));
			Assert.That(item.Title, Is.EqualTo("Track One"));
			Assert.That(item.Author, Is.EqualTo("Artist One"));
			Assert.That(item.Duration, Is.EqualTo("3:45"));
			Assert.That(item.Selected, Is.True);
			Assert.That(item.Thumbnails, Has.Count.EqualTo(1));
			Assert.That(item.Thumbnails[0].Url, Is.EqualTo("https://example.test/t1.jpg"));
		});

		Assert.That(state.Video, Is.Not.Null);
		var video = state.Video!;

		Assert.Multiple(() =>
		{
			Assert.That(video.Id, Is.EqualTo("video-1"));
			Assert.That(video.Title, Is.EqualTo("Track One"));
			Assert.That(video.Author, Is.EqualTo("Artist One"));
			Assert.That(video.ChannelId, Is.EqualTo("channel-1"));
			Assert.That(video.Album, Is.EqualTo("Album One"));
			Assert.That(video.AlbumId, Is.EqualTo("album-1"));
			Assert.That(video.LikeStatus, Is.EqualTo(YtmLikeStatus.Like));
			Assert.That(video.Thumbnails, Has.Count.EqualTo(2));
			Assert.That(video.DurationSeconds, Is.EqualTo(225));
			Assert.That(video.IsLive, Is.False);
			Assert.That(video.VideoType, Is.EqualTo(YtmVideoType.Audio));
			Assert.That(video.MetadataFilled, Is.True);
		});
	}

	[Test]
	public void Read_yields_null_for_fields_absent_on_a_pre_2_0_6_payload()
	{
		var state = YtmDesktopStateReader.Read(Parse(PreTwoZeroSixPayloadJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.Muted, Is.Null);
			Assert.That(state.Video, Is.Not.Null);
			Assert.That(state.Video!.IsLive, Is.Null);
			Assert.That(state.Video.VideoType, Is.Null);
			Assert.That(state.Video.MetadataFilled, Is.Null);
			Assert.That(state.Video.LikeStatus, Is.Null);
			Assert.That(state.Video.Album, Is.Null);
			Assert.That(state.Video.AlbumId, Is.Null);
		});
	}

	[Test]
	public void Read_parses_an_explicit_json_null_video_and_queue()
	{
		var state = YtmDesktopStateReader.Read(Parse(NullVideoAndQueueJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Video, Is.Null);
			Assert.That(state.Player.Queue, Is.Null);
			Assert.That(state.Player.TrackState, Is.EqualTo(YtmTrackState.Paused));
		});
	}

	[Test]
	public void Read_ignores_unknown_extra_properties()
	{
		var state = YtmDesktopStateReader.Read(Parse(UnknownExtraPropertiesJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.TrackState, Is.EqualTo(YtmTrackState.Playing));
			Assert.That(state.Video, Is.Not.Null);
			Assert.That(state.Video!.Id, Is.EqualTo("v"));
		});
	}

	[Test]
	public void Read_does_not_throw_when_a_number_field_holds_a_string()
	{
		var state = YtmDesktopStateReader.Read(Parse(WrongTypesJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.VideoProgress, Is.EqualTo(0));
			Assert.That(state.Player.Volume, Is.EqualTo(0));
		});
	}

	[Test]
	public void Read_maps_an_out_of_range_enum_integer_to_unknown()
	{
		var state = YtmDesktopStateReader.Read(Parse(OutOfRangeEnumsJson));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.TrackState, Is.EqualTo(YtmTrackState.Unknown));
			Assert.That(state.Player.Queue, Is.Not.Null);
			Assert.That(state.Player.Queue!.RepeatMode, Is.EqualTo(YtmRepeatMode.Unknown));
			Assert.That(state.Video, Is.Not.Null);
			Assert.That(state.Video!.LikeStatus, Is.EqualTo(YtmLikeStatus.Unknown));
			Assert.That(state.Video.VideoType, Is.EqualTo(YtmVideoType.Unknown));
		});
	}

	[Test]
	public void Read_parses_an_empty_object()
	{
		var state = YtmDesktopStateReader.Read(Parse("{}"));

		Assert.Multiple(() =>
		{
			Assert.That(state.Player.TrackState, Is.EqualTo(YtmTrackState.Unknown));
			Assert.That(state.Player.Queue, Is.Null);
			Assert.That(state.Video, Is.Null);
		});
	}

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
