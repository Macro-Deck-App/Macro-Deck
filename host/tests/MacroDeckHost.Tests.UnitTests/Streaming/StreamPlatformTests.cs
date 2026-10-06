using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch.Chat;
using MacroDeckHost.Tests.UnitTests.Variables;
using MacroDeckHost.Widgets.HistoryGraph;
using MacroDeckHost.Widgets.StreamChat;
using MacroDeckHost.Widgets.StreamStats;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Streaming;

[TestFixture]
internal sealed class StreamPlatformTests
{
	private const string SharedAccountId = "111";

	[Test]
	public void Twitch_widgets_keep_the_ids_and_data_stored_widgets_were_saved_with()
	{
		var twitch = StreamPlatforms.Twitch;

		Assert.Multiple(() =>
		{
			Assert.That(twitch.OwnerId, Is.EqualTo(TwitchIntegration.IntegrationId));
			Assert.That(twitch.ChatWidgetTypeId, Is.EqualTo("app.macro-deck.twitch::chat"));
			Assert.That(twitch.StatsWidgetTypeId, Is.EqualTo("app.macro-deck.twitch::stats"));
			Assert.That(twitch.DialogViewId, Is.EqualTo("twitch-chat"));
			Assert.That(twitch.ChatWidgetDescriptor().DefaultData, Is.EqualTo("""{"account":"","allowModeration":true}"""));
			Assert.That(twitch.ChatWidgetDescriptor().DataSchema, Is.EqualTo(
				"""{"type":"object","properties":{"account":{"type":"string"},"allowModeration":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"},"textSize":{"type":["number","null"],"minimum":25,"maximum":300,"description":"Chat text size in percent, 100 when unset"}}}"""));
			Assert.That(twitch.StatsWidgetDescriptor().DefaultData, Is.EqualTo(
				"""{"account":"","style":"overview","metric":"viewers","tiles":["viewers","chatters","followers"],"details":["title","category","uptime"],"showThumbnail":true}"""));
			Assert.That(twitch.StatsWidgetDescriptor().DataSchema, Is.EqualTo(
				"""{"type":"object","properties":{"account":{"type":"string"},"style":{"type":"string","enum":["overview","statsRow","liveRow","valueGraph","value"]},"metric":{"type":"string","enum":["viewers","chatters","followers","subscribers"]},"tiles":{"type":"array","items":{"type":"string","enum":["viewers","chatters","followers","subscribers"]}},"details":{"type":"array","items":{"type":"string","enum":["title","category","uptime"]}},"showThumbnail":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}"""));
		});
	}

	[Test]
	public void The_same_account_id_on_two_platforms_keeps_two_separate_chats()
	{
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch),
			StreamPlatformTestSupport.Set(StreamPlatformTestSupport.TestPlatform));
		var twitch = (StreamChatHub)services.Find(StreamPlatforms.Twitch.OwnerId)!.ChatFeed;
		var other = (StreamChatHub)services.Find(StreamPlatformTestSupport.TestPlatform.OwnerId)!.ChatFeed;

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
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch),
			StreamPlatformTestSupport.Set(StreamPlatformTestSupport.TestPlatform));
		var twitch = services.Find(StreamPlatforms.Twitch.OwnerId)!;
		var other = services.Find(StreamPlatformTestSupport.TestPlatform.OwnerId)!;

		twitch.StatsSink.SetAccounts([new StreamStatsAccount(SharedAccountId, "On Twitch", "twitch_streamer_")]);
		other.StatsSink.SetAccounts([new StreamStatsAccount(SharedAccountId, "Elsewhere", "other_streamer_")]);
		twitch.StatsSink.SetAccounts([]);

		Assert.Multiple(() =>
		{
			Assert.That(twitch.StatsAccounts.Accounts, Is.Empty);
			Assert.That(other.StatsAccounts.Accounts.Select(account => account.Label),
				Is.EqualTo(new[] { "Elsewhere" }));
		});
	}

	[Test]
	public void Each_integration_receives_only_the_sinks_of_the_platform_it_owns()
	{
		var twitchChat = new RecordingTwitchChatSink();
		var otherChat = new RecordingTwitchChatSink();
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch, chatSink: twitchChat),
			StreamPlatformTestSupport.Set(StreamPlatformTestSupport.TestPlatform, chatSink: otherChat));
		var twitch = new SinkConsumer(StreamPlatforms.Twitch.OwnerId);
		var other = new SinkConsumer(StreamPlatformTestSupport.TestPlatform.OwnerId);
		var unrelated = new SinkConsumer("app.macro-deck.unrelated");

		foreach (var integration in new[] { twitch, other, unrelated })
		{
			IntegrationGatewayBinder.Bind(integration,
				null!,
				null!,
				new InMemoryVariableBindingStore(),
				new VariableRefreshSignal(),
				streamPlatforms: services);
		}

		Assert.Multiple(() =>
		{
			Assert.That(twitch.ChatSink, Is.SameAs(twitchChat));
			Assert.That(twitch.StatsSink, Is.SameAs(services.Find(StreamPlatforms.Twitch.OwnerId)!.StatsSink));
			Assert.That(other.ChatSink, Is.SameAs(otherChat));
			Assert.That(other.StatsSink,
				Is.SameAs(services.Find(StreamPlatformTestSupport.TestPlatform.OwnerId)!.StatsSink));
			Assert.That(unrelated.ChatSink, Is.Null);
			Assert.That(unrelated.StatsSink, Is.Null);
		});
	}

	[Test]
	public async Task A_second_platforms_chat_and_stats_configuration_lists_only_its_own_accounts()
	{
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch),
			StreamPlatformTestSupport.Set(StreamPlatformTestSupport.TestPlatform));
		var twitch = services.Find(StreamPlatforms.Twitch.OwnerId)!;
		var other = services.Find(StreamPlatformTestSupport.TestPlatform.OwnerId)!;
		twitch.ChatSink.SetAccounts([new ChatAccount("111", "Twitch streamer")]);
		twitch.StatsSink.SetAccounts([new StreamStatsAccount("111", "Twitch streamer", "twitch_streamer_")]);
		other.ChatSink.SetAccounts([new ChatAccount("222", "Other streamer")]);
		other.StatsSink.SetAccounts([new StreamStatsAccount("222", "Other streamer", "other_streamer_")]);
		var provider = Provider(other, new FakeIntegrationRegistry());

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
		using var services = StreamPlatformTestSupport.Services(
			StreamPlatformTestSupport.Set(StreamPlatforms.Twitch),
			StreamPlatformTestSupport.Set(StreamPlatformTestSupport.TestPlatform));
		var provider = Provider(services.Find(StreamPlatformTestSupport.TestPlatform.OwnerId)!,
			new FakeIntegrationRegistry());

		var chatWidget = await provider.CreateSessionAsync(Request(WidgetSurface(StreamPlatforms.Twitch.ChatWidgetTypeId)),
			CancellationToken.None);
		var statsConfig = await provider.CreateSessionAsync(Request(ConfigSurface(StreamPlatforms.Twitch.StatsWidgetTypeId)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(provider.IntegrationId, Is.EqualTo(StreamPlatformTestSupport.TestPlatform.OwnerId));
			Assert.That(chatWidget, Is.Null);
			Assert.That(statsConfig, Is.Null);
		});
	}

	[Test]
	public async Task A_stored_twitch_chat_widget_without_an_account_shows_the_first_twitch_account()
	{
		using var services = StreamPlatformTestSupport.Services(StreamPlatformTestSupport.Set(StreamPlatforms.Twitch));
		var twitch = services.Find(StreamPlatforms.Twitch.OwnerId)!;
		var hub = (StreamChatHub)twitch.ChatFeed;
		hub.SetAccounts([new ChatAccount("111", "First"), new ChatAccount("222", "Second")]);
		hub.Post(new ChatConnectionChanged("111", true));
		hub.Post(new ChatConnectionChanged("222", true));
		hub.Post(new ChatMessageReceived("111", TwitchChatHubTests.Message("first") with
		{
			Fragments = [new ChatFragment(ChatFragmentKind.Text, "from the first channel")],
		}));
		hub.Post(new ChatMessageReceived("222", TwitchChatHubTests.Message("second") with
		{
			Fragments = [new ChatFragment(ChatFragmentKind.Text, "from the second channel")],
		}));
		hub.Tick();

		await using var session = await OpenAsync(Provider(twitch, new FakeIntegrationRegistry()),
			WidgetSurface("app.macro-deck.twitch::chat"));
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.Some.Contains("from the first channel"));
			Assert.That(texts, Has.None.Contains("from the second channel"));
		});
	}

	private static StreamChatWidgetUiProvider Provider(
		StreamPlatformServiceSet set,
		FakeIntegrationRegistry integrations)
	{
		var resources = new UiResourceStore();

		return new StreamChatWidgetUiProvider(set.Platform,
			set.ChatFeed,
			set.ChatImages,
			TestLocalization.SampleText,
			integrations,
			resources,
			new RecordingUiInteractions(),
			new FakeFolderCache(),
			new FakeHostLockState(),
			Logger.None,
			new StreamStatsWidgetUiProvider(set.Platform,
				set.StatsAccounts,
				set.Thumbnails,
				new VariableRegistry(),
				new FakeVariableHistory(),
				new VariableChangeNotifier(),
				TestLocalization.SampleText,
				integrations,
				resources));
	}

	private static async Task<IUiSession> OpenAsync(StreamChatWidgetUiProvider provider, UiSurface surface)
		=> (await provider.CreateSessionAsync(Request(surface), CancellationToken.None))!;

	private static UiSessionRequest Request(UiSurface surface) => new() { Surface = surface, UiModelVersion = 1 };

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

	private static UiSurface WidgetSurface(string widgetType)
		=> new()
		{
			Kind = UiSurfaceKinds.Preview,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" }),
			},
		};

	private static string[] AccountOptions(IUiSession session)
	{
		var account = Flatten(session.BuildTree().Root).First(node =>
			node.Id == StreamChatWidgetType.AccountKey && node.Properties.ContainsKey(UiConfigProperties.Options));

		return
		[
			.. account.Properties[UiConfigProperties.Options].EnumerateArray()
				.Select(option => option.GetProperty("value").GetString()!),
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
		=> [.. Flatten(root).Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
			.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())];

	private sealed class SinkConsumer(string id) : IIntegration, IStreamChatSinkConsumer, IStreamStatsSinkConsumer
	{
		public IStreamChatSink? ChatSink { get; private set; }

		public IStreamStatsSink? StatsSink { get; private set; }

		public string Id => id;

		public LocalizedText Name => "Platform";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public void UseStreamChatSink(IStreamChatSink sink) => ChatSink = sink;

		public void UseStreamStatsSink(IStreamStatsSink sink) => StatsSink = sink;
	}

	private sealed class FakeVariableHistory : IVariableHistory
	{
		public IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null)
			=> EmptyVariableHistoryWindow.Instance;
	}
}
