using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.YouTube;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeVariablesTests
{
	private static readonly string[] _expectedNames =
	[
		"youtube_streamer_display_name",
		"youtube_streamer_is_live",
		"youtube_streamer_stream_title",
		"youtube_streamer_viewer_count",
		"youtube_streamer_like_count",
		"youtube_streamer_subscriber_count",
		"youtube_streamer_uptime_seconds",
		"youtube_streamer_stream_thumbnail_url"
	];

	[Test]
	public void Every_variable_is_prefixed_with_youtube_and_the_channel_key()
	{
		var names = YouTubeVariables.Declare("streamer").Select(definition => definition.Name);

		Assert.That(names, Is.EquivalentTo(_expectedNames));
	}

	[Test]
	public void Numbers_are_numeric_and_the_live_flag_is_boolean()
	{
		var types = YouTubeVariables.Declare("streamer").ToDictionary(definition => definition.Name!, d => d.Type);

		Assert.Multiple(() =>
		{
			Assert.That(types["youtube_streamer_is_live"], Is.EqualTo(VariableType.Boolean));
			Assert.That(types["youtube_streamer_viewer_count"], Is.EqualTo(VariableType.Numeric));
			Assert.That(types["youtube_streamer_like_count"], Is.EqualTo(VariableType.Numeric));
			Assert.That(types["youtube_streamer_subscriber_count"], Is.EqualTo(VariableType.Numeric));
			Assert.That(types["youtube_streamer_uptime_seconds"], Is.EqualTo(VariableType.Numeric));
			Assert.That(types["youtube_streamer_stream_title"], Is.EqualTo(VariableType.Text));
		});
	}

	[TestCase("youtube_streamer_like_count", "streamer", "like_count")]
	[TestCase("youtube_my_channel_is_live", "my_channel", "is_live")]
	[TestCase("youtube_chuc123_stream_thumbnail_url", "chuc123", "stream_thumbnail_url")]
	public void A_variable_name_splits_back_into_channel_key_and_name(string variable, string key, string name)
		=> Assert.That(YouTubeVariables.Split(variable), Is.EqualTo((key, name)));

	[TestCase("twitch_streamer_viewer_count")]
	[TestCase("youtube_streamer_unknown")]
	public void Foreign_or_unknown_names_do_not_split(string variable)
		=> Assert.That(YouTubeVariables.Split(variable), Is.Null);

	[Test]
	public void Live_only_numbers_read_nothing_while_offline()
	{
		var account = YouTubeTestSupport.Account();
		var state = new YouTubeAccountState
		{
			IsLive = false,
			ViewerCount = 10,
			LikeCount = 3,
			SubscriberCount = 500,
			StreamStartedAt = YouTubeTestSupport.Now,
			StreamThumbnailUrl = "https://i.ytimg.com/vi/x/maxresdefault.jpg"
		};

		Assert.Multiple(() =>
		{
			Assert.That(YouTubeVariables.Read(account, state, "viewer_count", YouTubeTestSupport.Now), Is.Null);
			Assert.That(YouTubeVariables.Read(account, state, "like_count", YouTubeTestSupport.Now), Is.Null);
			Assert.That(YouTubeVariables.Read(account, state, "uptime_seconds", YouTubeTestSupport.Now), Is.Null);
			Assert.That(YouTubeVariables.Read(account, state, "stream_thumbnail_url", YouTubeTestSupport.Now),
				Is.Null);
			Assert.That(YouTubeVariables.Read(account, state, "subscriber_count", YouTubeTestSupport.Now),
				Is.EqualTo(500));
			Assert.That(YouTubeVariables.Read(account, state, "is_live", YouTubeTestSupport.Now), Is.False);
		});
	}

	[Test]
	public void The_display_name_falls_back_to_the_stored_title_until_youtube_reported_one()
	{
		var account = YouTubeTestSupport.Account(title: "Stored title");

		Assert.Multiple(() =>
		{
			Assert.That(YouTubeVariables.Read(account, YouTubeAccountState.Unknown, "display_name", YouTubeTestSupport.Now),
				Is.EqualTo("Stored title"));
			Assert.That(YouTubeVariables.Read(account, new YouTubeAccountState { ChannelTitle = "Renamed" },
					"display_name",
					YouTubeTestSupport.Now),
				Is.EqualTo("Renamed"));
		});
	}
}
