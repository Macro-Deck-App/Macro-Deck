using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Widgets;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Streaming;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatWidgetSessionTests : UiSessionFixture
{
	private const string ClientId = "client-a";
	private const string Connection = "conn-a";

	private readonly Guid _widgetId = Guid.Parse("4f0f9c56-5b8b-4d93-a3a9-2f7f3bd6a1e2");
	private StreamChatHub _hub = null!;
	private FakeStreamingContext _context = null!;
	private CountingModerator _moderator = null!;

	[SetUp]
	public void SetUpTwitchChat()
	{
		_hub = new StreamChatHub(new FakeTimeProvider(), Logger.None);
		_hub.SetAccounts([new ChatAccount("111", "Streamer (@streamer)")]);
		_hub.Post(new ChatConnectionChanged("111", true));
		_hub.Post(new ChatMessageReceived("111", TwitchChatHubTests.Message("m1")));
		_hub.Tick();

		_context = new FakeStreamingContext(TwitchIntegration.IntegrationId, typeof(TwitchIntegration));
		_context.OwnWidgets.Store(_widgetId, TwitchStreamPlatform.Platform.ChatWidgetTypeId, """{"account":""}""");
		_moderator = new CountingModerator();

		var provider = StreamPlatformTestSupport.Provider(TwitchStreamPlatform.Platform,
			_hub,
			new FakeTwitchChatImages(),
			context: _context,
			moderator: _moderator);

		Integrations.Add(new StreamingUiIntegration(TwitchIntegration.IntegrationId, provider));
	}

	[TearDown]
	public void TearDownTwitchChat()
	{
		_hub.Dispose();
	}

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
		var tree = await OpenTreeAsync(WidgetSurface(UiSurfaceKinds.Preview, true));

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
				[UiConfigSurfaceAttributes.WidgetType] = Json(TwitchStreamPlatform.Platform.ChatWidgetTypeId),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(new { account = "" })
			}
		};

		var tree = await OpenTreeAsync(surface);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("\"111\""));
			Assert.That(tree, Does.Contain("Streamer (@streamer)"));
			Assert.That(tree, Does.Contain("Integrations.StreamChat.Widget.FirstAccount"));
		});
	}

	[Test]
	public async Task The_configuration_offers_the_stored_text_size()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = Json(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = Json(TwitchStreamPlatform.Platform.ChatWidgetTypeId),
				[UiConfigSurfaceAttributes.WidgetData]
					= JsonSerializer.SerializeToElement(new { account = "", textSize = 150 })
			}
		};

		var tree = await OpenTreeAsync(surface);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("\"textSize\""));
			Assert.That(tree, Does.Contain("150"));
		});
	}

	[Test]
	public async Task The_configuration_offers_the_stored_chat_colours()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = Json(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = Json(TwitchStreamPlatform.Platform.ChatWidgetTypeId),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(new
					{ account = "", messageColor = "#ffcc00", nameColor = "#00ff00" })
			}
		};

		var tree = await OpenTreeAsync(surface);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("\"messageColor\""));
			Assert.That(tree, Does.Contain("#ffcc00"));
			Assert.That(tree, Does.Contain("Integrations.StreamChat.Widget.MessageColor"));
			Assert.That(tree, Does.Contain("\"nameColor\""));
			Assert.That(tree, Does.Contain("#00ff00"));
			Assert.That(tree, Does.Contain("Integrations.StreamChat.Widget.NameColorDescription"));
		});
	}

	[Test]
	public async Task A_press_opens_the_chat_dialog_on_the_client_that_pressed()
	{
		var (sessionId, root) = await OpenAttachedAsync(WidgetSurface(UiSurfaceKinds.Widget, widgetId: _widgetId));

		var response = Press(sessionId, root);
		await WaitForAsync(() => Interactions.Opened.Count == 1, "the dialog never opened");

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(Interactions.Opened.Single().IntegrationId, Is.EqualTo(TwitchIntegration.IntegrationId));
			Assert.That(Interactions.Opened.Single().ClientId, Is.EqualTo(ClientId));
			Assert.That(Interactions.Opened.Single().Modal.ViewId,
				Is.EqualTo(TwitchStreamPlatform.Platform.DialogViewId));
		});
	}

	[Test]
	public async Task A_press_while_the_host_is_locked_opens_no_dialog()
	{
		var (sessionId, root) = await OpenAttachedAsync(WidgetSurface(UiSurfaceKinds.Widget, widgetId: _widgetId));
		HostLock.IsLocked = true;

		var response = Press(sessionId, root);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.HostLocked));
			Assert.That(Interactions.Opened, Is.Empty);
		});
	}

	[Test]
	public async Task Nothing_in_the_chat_dialog_is_moderated_while_the_host_is_locked()
	{
		var (sessionId, root) = await OpenAttachedAsync(DialogSurface());
		var line = CalendarTree.Flatten(root).First(CalendarTree.IsPressable);
		HostLock.IsLocked = true;

		var response = Press(sessionId, line.Id);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.HostLocked));
			Assert.That(_moderator.Calls, Is.Zero);
		});
	}

	[Test]
	public async Task A_surface_for_another_widget_type_is_declined()
	{
		var surface = WidgetSurface(UiSurfaceKinds.Widget) with
		{
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetType] = Json("app.macro-deck.twitch::other")
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

	private async Task<(string SessionId, UiNode Root)> OpenAttachedAsync(UiSurface surface)
	{
		var ticket = await Broker.OpenAsync(TwitchIntegration.IntegrationId, surface, DeviceA, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		Assert.That(Attach(ticket.SessionId, Connection).Accepted, Is.True);

		var tree = await Broker.FirstTreeAsync(ticket.SessionId, CancellationToken.None);
		var root = JsonSerializer.Deserialize<UiTree>(tree!.Value.Utf8.Span, UiCanonicalJson.Options)!.Root;
		return (ticket.SessionId, root);
	}

	private UiSendEventResponse Press(string sessionId, UiNode node)
	{
		return Press(sessionId, node.Id);
	}

	private UiSendEventResponse Press(string sessionId, string nodeId)
	{
		return Broker.SendEvent(new UiSendEventRequest
				{ SessionId = sessionId, NodeId = nodeId, Name = UiComponentEvents.Press },
			Connection,
			ClientId);
	}

	private static UiSurface DialogSurface()
	{
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ModalId] = Json("modal-1"),
				[UiDialogSurfaceAttributes.ViewId] = Json(TwitchStreamPlatform.Platform.DialogViewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					["account"] = "111", ["widgetId"] = "4f0f9c56-5b8b-4d93-a3a9-2f7f3bd6a1e2"
				})
			}
		};
	}

	private static JsonElement Json<T>(T value)
	{
		return JsonSerializer.SerializeToElement(value);
	}

	private static UiSurface WidgetSurface(string kind, bool sample = false, Guid? widgetId = null)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = Json(TwitchStreamPlatform.Platform.ChatWidgetTypeId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" })
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		if (widgetId is { } id)
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = Json(id.ToString());
		}

		return new UiSurface { Kind = kind, SessionMode = UiSessionModes.Shared, Attributes = attributes };
	}

	private sealed class CountingModerator : IStreamChatModerator
	{
		private int _calls;

		public int Calls => Volatile.Read(ref _calls);

		public Task<ChatModerationResult> ModerateAsync(
			string accountId,
			ChatModerationRequest request,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			return Task.FromResult(ChatModerationResult.Succeeded);
		}
	}
}
