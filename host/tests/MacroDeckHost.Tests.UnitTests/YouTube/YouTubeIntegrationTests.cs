using System.Text;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeIntegrationTests
{
	[Test]
	public void The_integration_is_identified_and_discovered_by_the_host()
	{
		using var integration = new YouTubeIntegration();

		var discovered = IntegrationDiscovery.DiscoverIntegrations(Log.Logger).OfType<YouTubeIntegration>();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Id, Is.EqualTo("app.macro-deck.youtube"));
			Assert.That(integration.Id, Is.EqualTo(StreamPlatforms.YouTube.OwnerId));
			Assert.That(TestLocalization.Resolve(integration.Name), Is.EqualTo("YouTube"));
			Assert.That(integration.AllowsMultipleConfigurations, Is.True);
			Assert.That(discovered, Has.Exactly(1).Items);
		});
	}

	[Test]
	public void The_brand_icon_is_a_red_svg()
	{
		using var integration = new YouTubeIntegration();

		var icon = Encoding.UTF8.GetString(integration.GetIcon());

		Assert.Multiple(() =>
		{
			Assert.That(integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(icon, Does.Contain("<svg"));
			Assert.That(icon, Does.Not.Contain("<style"));
		});
	}

	[Test]
	public void Without_a_channel_no_widget_is_offered_and_variables_are_a_template()
	{
		using var integration = new YouTubeIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.GetWidgetTypes(), Is.Empty);
			Assert.That(integration.Variables, Is.Empty);
			Assert.That(integration.DeclaredVariables.Select(v => v.Name),
				Is.All.Contains(VariableNameTemplate.Placeholder("account")));
		});
	}

	[Test]
	public async Task With_a_channel_the_youtube_chat_and_stats_widgets_are_offered()
	{
		using var integration = await ConnectedAsync();

		var types = integration.GetWidgetTypes();

		Assert.Multiple(() =>
		{
			Assert.That(types.Select(type => type.Id), Is.EqualTo(new[] { "chat", "stats" }));
			Assert.That(types[0].DefaultData, Is.EqualTo(StreamPlatforms.YouTube.ChatWidgetDescriptor().DefaultData));
			Assert.That(types[1].DefaultData, Is.EqualTo(StreamPlatforms.YouTube.StatsWidgetDescriptor().DefaultData));
			Assert.That(types[1].DataSchema, Is.EqualTo(StreamPlatforms.YouTube.StatsWidgetDescriptor().DataSchema));
			Assert.That(TestLocalization.Resolve(types[0].Name), Is.EqualTo("YouTube Chat"));
		});
	}

	[Test]
	public async Task Variables_are_declared_per_channel_and_read_from_its_state()
	{
		using var integration = await ConnectedAsync();
		integration.AccountManager.Connections[0].Merge(state => state with
		{
			IsLive = true,
			LikeCount = 12,
			ViewerCount = null
		});

		var likes = await integration.ReadAsync("youtube-mychannel-like-count");
		var viewers = await integration.ReadAsync("youtube-mychannel-viewer-count");
		var unknown = await integration.ReadAsync("youtube-somebody-like-count");

		Assert.Multiple(() =>
		{
			Assert.That(integration.DeclaredVariables, Is.EqualTo(YouTubeVariables.Declare("mychannel",
				integration.DeclaredVariables[0].Configuration)));
			Assert.That(integration.DeclaredVariables[0].Configuration?.Name.Literal, Is.EqualTo("My Channel (@mychannel)"));
			Assert.That(likes.Value, Is.EqualTo(12));
			Assert.That(viewers, Is.EqualTo(VariableReading.Unavailable));
			Assert.That(unknown, Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task Event_filters_offer_the_channels_and_the_event_types()
	{
		using var integration = await ConnectedAsync();

		var channels = await integration.GetEventOptionsAsync(Options("account"), CancellationToken.None);
		var types = await integration.GetEventOptionsAsync(Options("type"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(channels.Options.Select(option => option.Value), Is.EqualTo(new[] { "UCmine" }));
			Assert.That(types.Options.Select(option => option.Value), Has.Member("super-chat"));
			Assert.That(types.Options.Select(option => option.Value), Has.No.Member("event"));
		});
	}

	[Test]
	public async Task An_expired_sign_in_is_resolved_by_connecting_again()
	{
		using var integration = new YouTubeIntegration(YouTubeTestSupport.Manager(_ => new FakeYouTubeApiClient()));

		var token = await integration.ResolveIssueAsync("token-invalid:UCmine");
		var other = await integration.ResolveIssueAsync("quota-exhausted:client");

		Assert.Multiple(() =>
		{
			Assert.That(token.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
			Assert.That(other.Success, Is.False);
		});
	}

	[Test]
	public async Task Initializing_hands_the_channels_to_the_youtube_chat_and_stats()
	{
		var chat = new RecordingYouTubeChatSink();
		var stats = new RecordingStatsSink();
		var config = new RecordingIntegrationConfig();
		YouTubeTestSupport.AddChannel(config, "UCmine", "My Channel", "@mychannel");
		using var integration = new YouTubeIntegration(YouTubeTestSupport.Manager(_ => new FakeYouTubeApiClient()));
		integration.UseStreamChatSink(chat);
		integration.UseStreamStatsSink(stats);

		await integration.InitializeAsync(new Context(config));
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(chat.AccountLists[0].Select(account => account.AccountId), Is.EqualTo(new[] { "UCmine" }));
			Assert.That(stats.Lists[0].Single().VariablePrefix, Is.EqualTo("youtube_mychannel_"));
			Assert.That(chat.AccountLists[^1], Is.Empty, "shutting down hands the accounts back");
		});
	}

	private static async Task<YouTubeIntegration> ConnectedAsync()
	{
		var config = new RecordingIntegrationConfig();
		YouTubeTestSupport.AddChannel(config, "UCmine", "My Channel", "@mychannel");
		var manager = YouTubeTestSupport.Manager(_ => new FakeYouTubeApiClient());
		await manager.ReloadAsync(config);
		return new YouTubeIntegration(manager, time: new YouTubeManualClock(YouTubeTestSupport.Now));
	}

	private static EventOptionsContext Options(string parameterName)
		=> new() { EventId = "super-chat", ParameterName = parameterName };

	private sealed class RecordingStatsSink : MacroDeckHost.Application.StreamStats.IStreamStatsSink
	{
		public List<IReadOnlyList<MacroDeckHost.Application.StreamStats.StreamStatsAccount>> Lists { get; } = [];

		public void SetAccounts(IReadOnlyList<MacroDeckHost.Application.StreamStats.StreamStatsAccount> accounts)
			=> Lists.Add(accounts);
	}

	private sealed class Context(RecordingIntegrationConfig config) : IIntegrationContext
	{
		public IIntegrationConfig Config => config;

		public IEventPublisher Events { get; } = new RecordingYouTubeEventPublisher();

		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
