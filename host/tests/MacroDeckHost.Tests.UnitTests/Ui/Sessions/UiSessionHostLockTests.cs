using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionHostLockTests : UiSessionFixture
{
	private const string PluginId = "com.example.lockable";
	private const string ClientId = "client-a";
	private const string Connection = "conn-a";
	private static readonly Guid _widgetId = Guid.Parse("4c7a0f4e-1d8e-4f53-9d2b-8a31f0e7b6c1");

	private static UiSurface WidgetSurface()
	{
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString()),
				[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(PluginId + "::counter")
			}
		};
	}

	private static UiSurface DialogSurface()
	{
		return new UiSurface { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive };
	}

	private async Task<(string SessionId, ScriptedUiPluginInvoker Plugin)> OpenPluginWidgetAsync()
	{
		await RegisterPluginSessionAsync(PluginId);
		var plugin = new ScriptedUiPluginInvoker(() => Broker);
		Resolver.Add(new RemoteUiSessionProvider(PluginId, plugin));

		var ticket = await Broker.OpenAsync(PluginId, WidgetSurface(), DeviceA, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, "The plugin's widget session did not open.");
		Assert.That(Attach(ticket.SessionId, Connection).Accepted, Is.True);
		return (ticket.SessionId, plugin);
	}

	private UiSendEventResponse Send(string sessionId, string name)
	{
		return Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = name },
			Connection,
			ClientId);
	}

	private IReadOnlyList<ActionExecutionStatusEvent> StatusEventsToClient()
	{
		return
		[
			.. Transport.InGroup(UiClientGroups.For(ClientId))
				.Select(recorded => recorded.Message)
				.OfType<ActionExecutionStatusEvent>()
		];
	}

	[Test]
	public async Task A_plugin_widget_does_not_receive_a_client_press_while_the_host_is_locked()
	{
		var (sessionId, plugin) = await OpenPluginWidgetAsync();
		HostLock.IsLocked = true;

		var response = Send(sessionId, UiComponentEvents.Press);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.HostLocked));
			Assert.That(plugin.Events, Is.Empty, "The press reached the plugin while the host was locked.");
			Assert.That(StatusEventsToClient(), Has.Count.EqualTo(1));
			Assert.That(StatusEventsToClient().Single().Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(StatusEventsToClient().Single().Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(StatusEventsToClient().Single().WidgetId, Is.EqualTo(_widgetId.ToString()));
		});
	}

	[Test]
	public async Task A_plugin_widget_still_learns_it_was_revealed_while_the_host_is_locked()
	{
		var (sessionId, plugin) = await OpenPluginWidgetAsync();
		HostLock.IsLocked = true;

		var response = Send(sessionId, UiComponentEvents.Reveal);
		await WaitForAsync(() => plugin.Events.Count == 1, "The reveal never reached the plugin.");

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(StatusEventsToClient(), Is.Empty);
		});
	}

	[Test]
	public async Task A_plugin_widget_receives_the_press_once_the_host_is_unlocked()
	{
		var (sessionId, plugin) = await OpenPluginWidgetAsync();
		HostLock.IsLocked = true;
		Send(sessionId, UiComponentEvents.Press);
		HostLock.IsLocked = false;

		var response = Send(sessionId, UiComponentEvents.Press);
		await WaitForAsync(() => plugin.Events.Count == 1, "The press never reached the plugin.");

		Assert.That(response.Accepted, Is.True);
	}

	[Test]
	public async Task An_integration_served_through_the_sdk_is_refused_like_a_plugin_while_locked()
	{
		var session = new StubUiSession { Tree = () => TreeAt(1, UiSessionModes.Shared) };
		Integrations.Add(new StubUiIntegration("app.example.integration")
		{
			Session = session,
			Surfaces =
			[
				new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }
			]
		});
		var ticket = await Broker.OpenAsync("app.example.integration",
			DialogSurface(),
			DeviceA,
			CancellationToken.None);
		Attach(ticket.SessionId, Connection);
		HostLock.IsLocked = true;

		var response = Send(ticket.SessionId, UiComponentEvents.Tap);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.HostLocked));
			Assert.That(session.Dispatched, Is.Empty);
			Assert.That(StatusEventsToClient().Single().WidgetId, Is.Null);
		});
	}

	[Test]
	public async Task A_system_integration_served_through_the_sdk_keeps_its_own_policy_while_locked()
	{
		var session = new StubUiSession { Tree = () => TreeAt(1) };
		Integrations.Add(new StubSystemUiIntegration("app.example.system") { Session = session });
		var ticket = await Broker.OpenAsync("app.example.system", DialogSurface(), DeviceA, CancellationToken.None);
		Attach(ticket.SessionId, Connection);
		HostLock.IsLocked = true;

		var response = Send(ticket.SessionId, UiComponentEvents.Tap);
		await WaitForAsync(() => session.Dispatched.Count == 1, "The system dialog never received the tap.");

		Assert.That(response.Accepted, Is.True);
	}

	[TestCase("app.macro-deck.calendar")]
	[TestCase("app.macro-deck.music-player")]
	public async Task A_host_dialog_keeps_its_own_policy_while_locked(string integrationId)
	{
		var session = new StubUiSession { Tree = () => TreeAt(1) };
		BuiltInIntegrationProviders.Add(new StubBuiltInIntegrationUiProvider(integrationId, session));
		var ticket = await Broker.OpenAsync(integrationId, DialogSurface(), DeviceA, CancellationToken.None);
		Attach(ticket.SessionId, Connection);
		HostLock.IsLocked = true;

		var response = Send(ticket.SessionId, UiComponentEvents.Tap);
		await WaitForAsync(() => session.Dispatched.Count == 1, "The host dialog never received the tap.");

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(StatusEventsToClient(), Is.Empty);
		});
	}

	private sealed class StubSystemUiIntegration(string id) : IIntegration, ISystemIntegration, IUiProvider
	{
		public string Id { get; } = id;

		public bool IsActive => true;

		public LocalizedText Name => Id;

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public StubUiSession? Session { get; init; }

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
			[new() { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(Session);
		}
	}

	private sealed class StubBuiltInIntegrationUiProvider(string integrationId, StubUiSession session)
		: IBuiltInIntegrationUiProvider
	{
		public string IntegrationId { get; } = integrationId;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
			[new() { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(session);
		}
	}
}

internal sealed class ScriptedUiPluginInvoker(Func<IUiSessionSink> sink) : IPluginCapabilityInvoker
{
	private readonly Lock _gate = new();
	private readonly List<UiSessionEventArguments> _events = [];

	public IReadOnlyList<UiSessionEventArguments> Events
	{
		get
		{
			lock (_gate)
			{
				return [.. _events];
			}
		}
	}

	public Task<JsonElement?> InvokeAsync(
		string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken)
	{
		switch (request.Arguments)
		{
			case UiSessionOpenArguments:
				return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
					new UiSessionOpenResult { Accepted = true },
					PluginProtocolJson.Options));

			case UiSessionSnapshotArguments snapshot:
				sink().PublishSnapshot(pluginId, snapshot.SessionId, new UiRawJson(UiPayloads.Tree(1)));
				break;

			case UiSessionEventArguments evt:
				lock (_gate)
				{
					_events.Add(evt);
				}

				break;
		}

		return Task.FromResult<JsonElement?>(null);
	}

	public bool TryComplete(string pluginId, ProtocolEnvelope result)
	{
		return false;
	}

	public void AbortAll(string pluginId, ProtocolError reason)
	{
	}

	public bool IsLiveActionExecute(string pluginId, string correlationId)
	{
		return false;
	}
}
