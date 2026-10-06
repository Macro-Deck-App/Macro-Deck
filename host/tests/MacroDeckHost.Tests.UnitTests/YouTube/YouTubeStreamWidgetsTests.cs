using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Streaming;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch.Chat;
using MacroDeckHost.Widgets.HistoryGraph;
using MacroDeckHost.Widgets.StreamChat;
using MacroDeckHost.Widgets.StreamStats;
using Serilog.Core;
using DomainEnums = MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeStreamWidgetsTests
{
	[Test]
	public void The_youtube_platform_belongs_to_the_integration_and_has_its_own_widgets()
	{
		var youtube = StreamPlatforms.YouTube;

		Assert.Multiple(() =>
		{
			Assert.That(StreamPlatforms.All, Has.Member(youtube));
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
		var rule = StreamPlatforms.YouTube.Thumbnails!;
		var uri = new Uri(url);

		var accepted = string.Equals(uri.Host, rule.AllowedHost, StringComparison.OrdinalIgnoreCase) &&
			uri.AbsolutePath.StartsWith(rule.AllowedPathPrefix, StringComparison.Ordinal) &&
			rule.IsValidAccountId(channelId);

		Assert.That(accepted, Is.EqualTo(allowed));
	}

	[Test]
	public void The_stats_widget_reads_the_youtube_variables_of_the_channel()
	{
		var variables = new VariableRegistry();
		var account = new StreamStatsAccount("UCmine", "My Channel", "youtube_mine_");
		Set(variables, "youtube_mine_is_live", "true");
		Set(variables, "youtube_mine_viewer_count", "1234");
		Set(variables, "youtube_mine_like_count", "318");
		Set(variables, "youtube_mine_subscriber_count", "12400");
		Set(variables, "youtube_mine_stream_title", "Speedrun night");
		Set(variables, "youtube_mine_uptime_seconds", "3600");

		var state = StreamStatsResolver.Resolve(StreamPlatforms.YouTube, variables, account, [],
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
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch),
			StreamPlatformTestSupport.Set(StreamPlatforms.YouTube));
		var twitch = services.Find(StreamPlatforms.Twitch.OwnerId)!;
		var youtube = services.Find(StreamPlatforms.YouTube.OwnerId)!;
		twitch.ChatSink.SetAccounts([new ChatAccount("111", "Twitch streamer")]);
		youtube.ChatSink.SetAccounts([new ChatAccount("UCmine", "My Channel")]);
		var resources = new UiResourceStore();
		var provider = new StreamChatWidgetUiProvider(youtube.Platform,
			youtube.ChatFeed,
			youtube.ChatImages,
			TestLocalization.SampleText,
			new FakeIntegrationRegistry(),
			resources,
			new RecordingUiInteractions(),
			new FakeFolderCache(),
			new FakeHostLockState(),
			Logger.None,
			new StreamStatsWidgetUiProvider(youtube.Platform,
				youtube.StatsAccounts,
				youtube.Thumbnails,
				new VariableRegistry(),
				new EmptyHistory(),
				new VariableChangeNotifier(),
				TestLocalization.SampleText,
				new FakeIntegrationRegistry(),
				resources));

		await using var session = (await provider.CreateSessionAsync(new UiSessionRequest
		{
			Surface = ConfigSurface(StreamPlatforms.YouTube.ChatWidgetTypeId),
			UiModelVersion = 1
		}, CancellationToken.None))!;

		var account = Flatten(session.BuildTree().Root).First(node =>
			node.Id == StreamChatWidgetType.AccountKey && node.Properties.ContainsKey(UiConfigProperties.Options));
		var values = account.Properties[UiConfigProperties.Options].EnumerateArray()
			.Select(option => option.GetProperty("value").GetString())
			.ToList();

		Assert.That(values, Is.EqualTo(new[] { "UCmine" }));
	}

	private static void Set(VariableRegistry variables, string name, string value)
		=> variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Text,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = YouTubeIntegration.IntegrationId,
			Value = value,
			UpdatedAt = DateTime.UtcNow,
		});

	private static UiSurface ConfigSurface(string widgetType)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] =
					JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(new { account = "" }),
			},
		};

	private static IEnumerable<UiNode> Flatten(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Flatten))
		{
			yield return child;
		}
	}

	private sealed class EmptyHistory : IVariableHistory
	{
		public IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null)
			=> EmptyVariableHistoryWindow.Instance;
	}
}
