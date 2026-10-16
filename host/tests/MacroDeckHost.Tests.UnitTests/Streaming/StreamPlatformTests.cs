using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Widgets;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Widgets;
using MacroDeckHost.Tests.UnitTests.Twitch.Chat;
using MacroDeckHost.Integrations.Streaming.Widgets;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Streaming;

[TestFixture]
internal sealed class StreamPlatformTests
{
	private const string SharedAccountId = "111";

	[Test]
	public void Twitch_widgets_keep_the_ids_and_data_stored_widgets_were_saved_with()
	{
		var twitch = TwitchStreamPlatform.Platform;

		Assert.Multiple(() =>
		{
			Assert.That(twitch.OwnerId, Is.EqualTo(TwitchIntegration.IntegrationId));
			Assert.That(twitch.ChatWidgetTypeId, Is.EqualTo("app.macro-deck.twitch::chat"));
			Assert.That(twitch.StatsWidgetTypeId, Is.EqualTo("app.macro-deck.twitch::stats"));
			Assert.That(twitch.DialogViewId, Is.EqualTo("twitch-chat"));
			Assert.That(twitch.ChatWidgetDescriptor().DefaultData,
				Is.EqualTo("""{"account":"","allowModeration":true}"""));
			Assert.That(twitch.ChatWidgetDescriptor().DataSchema,
				Is.EqualTo(
					"""{"type":"object","properties":{"account":{"type":"string"},"allowModeration":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"},"textSize":{"type":["number","null"],"minimum":25,"maximum":300,"description":"Chat text size in percent, 100 when unset"},"messageColor":{"type":["string","null"],"description":"#rrggbb for the chat message text, the theme text colour when unset"},"nameColor":{"type":["string","null"],"description":"#rrggbb for every chatter name, each chatter's own colour when unset"}}}"""));
			Assert.That(twitch.StatsWidgetDescriptor().DefaultData,
				Is.EqualTo(
					"""{"account":"","style":"overview","metric":"viewers","tiles":["viewers","chatters","followers"],"details":["title","category","uptime"],"showThumbnail":true}"""));
			Assert.That(twitch.StatsWidgetDescriptor().DataSchema,
				Is.EqualTo(
					"""{"type":"object","properties":{"account":{"type":"string"},"style":{"type":"string","enum":["overview","statsRow","liveRow","valueGraph","value"]},"metric":{"type":"string","enum":["viewers","chatters","followers","subscribers"]},"tiles":{"type":"array","items":{"type":"string","enum":["viewers","chatters","followers","subscribers"]}},"details":{"type":"array","items":{"type":"string","enum":["title","category","uptime"]}},"showThumbnail":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}"""));
		});
	}

	[Test]
	public void The_same_account_id_on_two_platforms_keeps_two_separate_chats()
	{
		using var twitchIntegration = new TwitchIntegration();
		using var youTubeIntegration = new YouTubeIntegration();
		var twitch = twitchIntegration.Chat;
		var other = youTubeIntegration.Chat;

		twitch.SetAccounts([new ChatAccount(SharedAccountId, "On Twitch")]);
		other.SetAccounts([new ChatAccount(SharedAccountId, "Elsewhere")]);
		twitch.Post(new ChatMessageReceived(SharedAccountId, TwitchChatHubTests.Message("twitch-message")));
		other.Post(new ChatMessageReceived(SharedAccountId, TwitchChatHubTests.Message("other-message")));
		twitch.Tick();
		other.Tick();

		twitch.SetAccounts([]);
		twitch.Tick();

		var remaining = other.Snapshot(SharedAccountId);

		Assert.Multiple(() =>
		{
			Assert.That(twitch.Snapshot(SharedAccountId), Is.SameAs(ChatSnapshot.None));
			Assert.That(remaining.Account?.Label, Is.EqualTo("Elsewhere"));
			Assert.That(remaining.Messages.Select(message => message.MessageId), Is.EqualTo(new[] { "other-message" }));
		});
	}

	[Test]
	public void Replacing_one_platforms_stats_accounts_leaves_the_others_untouched()
	{
		var twitch = new StreamStatsSource((_, _) => null);
		var other = new StreamStatsSource((_, _) => null);

		twitch.SetAccounts([new StreamStatsAccount(SharedAccountId, "On Twitch", "twitch_streamer_")]);
		other.SetAccounts([new StreamStatsAccount(SharedAccountId, "Elsewhere", "other_streamer_")]);
		twitch.SetAccounts([]);

		Assert.Multiple(() =>
		{
			Assert.That(twitch.Accounts, Is.Empty);
			Assert.That(other.Accounts.Select(account => account.Label), Is.EqualTo(new[] { "Elsewhere" }));
		});
	}

	[Test]
	public async Task Each_integration_serves_only_the_widgets_of_the_platform_it_owns()
	{
		using var twitch = new TwitchIntegration();
		using var youTube = new YouTubeIntegration();

		var twitchServesYouTube = await twitch.CreateSessionAsync(
			Request(WidgetSurface(YouTubeStreamPlatform.Platform.ChatWidgetTypeId)),
			CancellationToken.None);
		var youTubeServesTwitch = await youTube.CreateSessionAsync(
			Request(WidgetSurface(TwitchStreamPlatform.Platform.StatsWidgetTypeId)),
			CancellationToken.None);
		await using var own = await twitch.CreateSessionAsync(
			Request(WidgetSurface(TwitchStreamPlatform.Platform.ChatWidgetTypeId)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(twitchServesYouTube, Is.Null);
			Assert.That(youTubeServesTwitch, Is.Null);
			Assert.That(own, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_second_platforms_chat_and_stats_configuration_lists_only_its_own_accounts()
	{
		using var twitchChat = new StreamChatHub(TimeProvider.System, Logger.None);
		using var otherChat = new StreamChatHub(TimeProvider.System, Logger.None);
		var twitchStats = new StreamStatsSource((_, _) => null);
		var otherStats = new StreamStatsSource((_, _) => null);
		twitchChat.SetAccounts([new ChatAccount("111", "Twitch streamer")]);
		twitchStats.SetAccounts([new StreamStatsAccount("111", "Twitch streamer", "twitch_streamer_")]);
		otherChat.SetAccounts([new ChatAccount("222", "Other streamer")]);
		otherStats.SetAccounts([new StreamStatsAccount("222", "Other streamer", "other_streamer_")]);
		var provider = StreamPlatformTestSupport.Provider(StreamPlatformTestSupport.TestPlatform,
			otherChat,
			stats: otherStats);

		await using var chat = await OpenAsync(provider,
			ConfigSurface(StreamPlatformTestSupport.TestPlatform.ChatWidgetTypeId));
		await using var stats = await OpenAsync(provider,
			ConfigSurface(StreamPlatformTestSupport.TestPlatform.StatsWidgetTypeId));

		Assert.Multiple(() =>
		{
			Assert.That(AccountOptions(chat), Is.EqualTo(new[] { "222" }));
			Assert.That(AccountOptions(stats), Is.EqualTo(new[] { "222" }));
		});
	}

	[Test]
	public async Task A_platform_ignores_the_widgets_and_dialogs_of_another_platform()
	{
		using var chat = new StreamChatHub(TimeProvider.System, Logger.None);
		var provider = StreamPlatformTestSupport.Provider(StreamPlatformTestSupport.TestPlatform, chat);

		var chatWidget = await provider.CreateSessionAsync(
			Request(WidgetSurface(TwitchStreamPlatform.Platform.ChatWidgetTypeId)),
			CancellationToken.None);
		var statsConfig = await provider.CreateSessionAsync(
			Request(ConfigSurface(TwitchStreamPlatform.Platform.StatsWidgetTypeId)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(chatWidget, Is.Null);
			Assert.That(statsConfig, Is.Null);
		});
	}

	[Test]
	public async Task A_stored_twitch_chat_widget_without_an_account_shows_the_first_twitch_account()
	{
		using var hub = new StreamChatHub(TimeProvider.System, Logger.None);
		hub.SetAccounts([new ChatAccount("111", "First"), new ChatAccount("222", "Second")]);
		hub.Post(new ChatConnectionChanged("111", true));
		hub.Post(new ChatConnectionChanged("222", true));
		hub.Post(new ChatMessageReceived("111",
			TwitchChatHubTests.Message("first") with
			{
				Fragments = [new ChatFragment(ChatFragmentKind.Text, "from the first channel")]
			}));
		hub.Post(new ChatMessageReceived("222",
			TwitchChatHubTests.Message("second") with
			{
				Fragments = [new ChatFragment(ChatFragmentKind.Text, "from the second channel")]
			}));
		hub.Tick();

		await using var session = await OpenAsync(
			StreamPlatformTestSupport.Provider(TwitchStreamPlatform.Platform, hub),
			WidgetSurface("app.macro-deck.twitch::chat"));
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.Some.Contains("from the first channel"));
			Assert.That(texts, Has.None.Contains("from the second channel"));
		});
	}

	private static async Task<IUiSession> OpenAsync(StreamWidgetsUiProvider provider, UiSurface surface)
	{
		return (await provider.CreateSessionAsync(Request(surface), CancellationToken.None))!;
	}

	private static UiSessionRequest Request(UiSurface surface)
	{
		return new UiSessionRequest { Surface = surface, UiModelVersion = 1 };
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

	private static UiSurface WidgetSurface(string widgetType)
	{
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Preview,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" })
			}
		};
	}

	private static string[] AccountOptions(IUiSession session)
	{
		var account = Flatten(session.BuildTree().Root)
			.First(node =>
				node.Id == StreamChatWidgetType.AccountKey && node.Properties.ContainsKey(UiConfigProperties.Options));

		return
		[
			.. account.Properties[UiConfigProperties.Options]
				.EnumerateArray()
				.Select(option => option.GetProperty("value").GetString()!)
		];
	}

	private static IEnumerable<UiNode> Flatten(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Flatten))
		{
			yield return child;
		}
	}

	private static List<string> Texts(UiNode root)
	{
		return
		[
			.. Flatten(root)
				.Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
				.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())
		];
	}
}
