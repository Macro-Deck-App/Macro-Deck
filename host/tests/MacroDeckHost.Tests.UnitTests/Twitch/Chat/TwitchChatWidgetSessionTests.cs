using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using MacroDeckHost.Widgets.TwitchChat;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

// Opens the chat widget the way a client does: through the broker and the real provider resolver, under
// the Twitch integration's own id, so a registration or surface mismatch fails here and not in the app.
[TestFixture]
internal sealed class TwitchChatWidgetSessionTests : UiSessionFixture
{
	private TwitchChatHub _hub = null!;

	[SetUp]
	public void SetUpTwitchChat()
	{
		_hub = new TwitchChatHub(new FakeTimeProvider(), Logger.None);
		_hub.SetAccounts([new TwitchChatAccount("111", "Streamer (@streamer)")]);
		_hub.Post(new TwitchChatConnectionChanged("111", true));
		_hub.Post(new TwitchChatMessageReceived("111", TwitchChatHubTests.Message("m1")));
		_hub.Tick();

		var provider = new TwitchChatWidgetUiProvider(_hub, new FakeTwitchChatImages(), TestLocalization.SampleText);

		Resolver.Fallback = new UiSessionProviderResolver(
			new RemoteUiProviderRegistry(new EmptyRemotePluginSnapshotStore(), Invoker),
			new UiProviderRegistry(Integrations, () => Broker, Logger.None),
			new ConfigFlowUiProviderRegistry(() => Broker, Logger.None),
			new ActionConfigUiProviderRegistry(Integrations, () => Broker, Logger.None),
			new WidgetUiProviderRegistry(new EmptyFolderCache(), [], () => Broker, Logger.None),
			new IntegrationUiProviderRegistry([provider], () => Broker, Logger.None),
			new UiPreviewProviderRegistry([], () => Broker, Logger.None));
	}

	[TearDown]
	public void TearDownTwitchChat() => _hub.Dispose();

	[Test]
	public async Task A_placed_chat_widget_opens_and_shows_the_chat()
	{
		var tree = await OpenTreeAsync(WidgetSurface(UiSurfaceKinds.Widget));

		Assert.That(tree, Does.Contain("Viewer: hello"));
	}

	[Test]
	public async Task The_editor_preview_opens_on_live_chat()
	{
		var tree = await OpenTreeAsync(WidgetSurface(UiSurfaceKinds.Preview));

		Assert.That(tree, Does.Contain("Viewer: hello"));
	}

	[Test]
	public async Task The_picker_sample_opens_without_live_chat()
	{
		var tree = await OpenTreeAsync(WidgetSurface(UiSurfaceKinds.Preview, sample: true));

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("PixelPanda"));
			Assert.That(tree, Does.Not.Contain("Viewer: hello"));
		});
	}

	[Test]
	public async Task The_configuration_offers_the_connected_accounts()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = Json(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = Json(TwitchChatWidgetType.QualifiedId),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(new { account = "" }),
			}
		};

		var tree = await OpenTreeAsync(surface);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("\"111\""));
			Assert.That(tree, Does.Contain("Streamer (@streamer)"));
			Assert.That(tree, Does.Contain("Integrations.Twitch.ChatWidget.FirstAccount"));
		});
	}

	[Test]
	public async Task A_surface_for_another_widget_type_is_declined()
	{
		var surface = WidgetSurface(UiSurfaceKinds.Widget) with
		{
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetType] = Json("app.macro-deck.twitch::other"),
			}
		};

		var ticket = await Broker.OpenAsync(TwitchIntegration.IntegrationId, surface, DeviceA, CancellationToken.None);

		Assert.That(ticket.Accepted, Is.False);
	}

	private async Task<string> OpenTreeAsync(UiSurface surface)
	{
		var ticket = await Broker.OpenAsync(TwitchIntegration.IntegrationId, surface, DeviceA, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);

		var tree = await Broker.FirstTreeAsync(ticket.SessionId, CancellationToken.None);
		Assert.That(tree, Is.Not.Null, "the session produced no tree");

		using var document = JsonDocument.Parse(tree!.Value.Utf8);
		return document.RootElement.GetRawText();
	}

	private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

	private static UiSurface WidgetSurface(string kind, bool sample = false)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = Json(TwitchChatWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" }),
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return new UiSurface { Kind = kind, SessionMode = UiSessionModes.Shared, Attributes = attributes };
	}
}
