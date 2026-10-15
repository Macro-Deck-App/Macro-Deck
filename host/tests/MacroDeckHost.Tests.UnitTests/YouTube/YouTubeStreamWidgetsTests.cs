using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Widgets;
using MacroDeckHost.Tests.UnitTests.Streaming;
using MacroDeckHost.Integrations.Streaming.Widgets;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeStreamWidgetsTests
{
	[Test]
	public void The_youtube_platform_belongs_to_the_integration_and_has_its_own_widgets()
	{
		var youtube = YouTubeStreamPlatform.Platform;

		Assert.Multiple(() =>
		{
			Assert.That(youtube.OwnerId, Is.EqualTo(YouTubeIntegration.IntegrationId));
			Assert.That(youtube.ChatWidgetTypeId, Is.EqualTo("app.macro-deck.youtube::chat"));
			Assert.That(youtube.StatsWidgetTypeId, Is.EqualTo("app.macro-deck.youtube::stats"));
			Assert.That(youtube.DialogViewId, Is.EqualTo("youtube-chat"));
			Assert.That(youtube.Stats.MetricIds, Is.EqualTo(new[] { "viewers", "likes", "subscribers" }));
			Assert.That(youtube.Stats.DefaultTiles, Is.EqualTo(new[] { "viewers", "likes", "subscribers" }));
			Assert.That(youtube.Stats.DetailIds, Is.EqualTo(new[] { "title", "uptime" }));
			Assert.That(youtube.Chat.Unsupported is not null, Is.True);
		});
	}

	[TestCase("https://i.ytimg.com/vi/abc/maxresdefault_live.jpg", "UCa-b_c", true)]
	[TestCase("https://i.ytimg.com/vi_webp/abc/maxresdefault.webp", "UCabc", false)]
	[TestCase("https://example.com/vi/abc/maxresdefault.jpg", "UCabc", false)]
	[TestCase("https://i.ytimg.com/vi/abc/maxresdefault.jpg", "UC/../x", false)]
	public void Thumbnails_come_only_from_youtubes_image_host(string url, string channelId, bool allowed)
	{
		var rule = YouTubeStreamPlatform.Platform.Thumbnails!;
		var uri = new Uri(url);

		var accepted = string.Equals(uri.Host, rule.AllowedHost, StringComparison.OrdinalIgnoreCase)
			&& uri.AbsolutePath.StartsWith(rule.AllowedPathPrefix, StringComparison.Ordinal)
			&& rule.IsValidAccountId(channelId);

		Assert.That(accepted, Is.EqualTo(allowed));
	}

	[Test]
	public void The_stats_widget_reads_the_youtube_values_of_the_channel()
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["is_live"] = true,
			["viewer_count"] = 1234L,
			["like_count"] = 318L,
			["subscriber_count"] = 12400L,
			["stream_title"] = "Speedrun night",
			["uptime_seconds"] = 3600L
		};
		var account = new StreamStatsAccount("UCmine", "My Channel", "youtube_mine_");
		var source = new StreamStatsSource((candidate, name)
			=> candidate.AccountId == "UCmine" ? values.GetValueOrDefault(name) : null);

		var state = StreamStatsResolver.Resolve(YouTubeStreamPlatform.Platform,
			source,
			account,
			[],
			new NoStreamThumbnails());

		Assert.Multiple(() =>
		{
			Assert.That(state.IsLive, Is.True);
			Assert.That(state.Metrics["viewers"], Is.EqualTo(StreamStatsResolver.Count("1234")));
			Assert.That(state.Metrics["likes"], Is.EqualTo("318"));
			Assert.That(state.Metrics["subscribers"], Is.EqualTo(StreamStatsResolver.Count("12400")));
			Assert.That(state.Details["title"], Is.EqualTo("Speedrun night"));
			Assert.That(state.Details["uptime"], Is.EqualTo("01:00:00"));
		});
	}

	[Test]
	public async Task The_youtube_chat_configuration_lists_only_youtube_channels()
	{
		using var twitch = new StreamChatHub(TimeProvider.System, Logger.None);
		using var youtube = new StreamChatHub(TimeProvider.System, Logger.None);
		twitch.SetAccounts([new ChatAccount("111", "Twitch streamer")]);
		youtube.SetAccounts([new ChatAccount("UCmine", "My Channel")]);
		var provider = StreamPlatformTestSupport.Provider(YouTubeStreamPlatform.Platform, youtube);

		await using var session = (await provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = ConfigSurface(YouTubeStreamPlatform.Platform.ChatWidgetTypeId),
				UiModelVersion = 1
			},
			CancellationToken.None))!;

		var account = Flatten(session.BuildTree().Root)
			.First(node =>
				node.Id == StreamChatWidgetType.AccountKey && node.Properties.ContainsKey(UiConfigProperties.Options));
		var values = account.Properties[UiConfigProperties.Options]
			.EnumerateArray()
			.Select(option => option.GetProperty("value").GetString())
			.ToList();

		Assert.That(values, Is.EqualTo(new[] { "UCmine" }));
	}

	private static UiSurface ConfigSurface(string widgetType)
	{
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] =
					JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(new { account = "" })
			}
		};
	}

	private static IEnumerable<UiNode> Flatten(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Flatten))
		{
			yield return child;
		}
	}
}
