using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.PreviewFixture;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
[NonParallelizable]
public class UiPreviewHotReloadTests
{
	private static readonly TimeSpan _quiet = TimeSpan.FromMilliseconds(300);

	[SetUp]
	public void ResetScenario()
	{
		HotReloadedViewPreviews.Text = "before";
		HotReloadedViewPreviews.Throws = false;
	}

	[Test]
	public async Task A_hot_reload_pushes_a_freshly_built_scenario_into_the_open_preview_and_asks_for_a_new_describe()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession());
		handler.DeclareCapabilities();

		var opened = await OpenPreviewAsync(handler, "preview-1");
		Assert.That(opened.Accepted, Is.True, opened.RejectionReason);

		HotReloadedViewPreviews.Text = "after";
		UiPreviewHotReload.UpdateApplication(null);

		var sent = await CollectAsync(socket, 2);
		var snapshot = sent.Single(envelope => envelope.Type == MessageTypes.HostInvoke);
		var stateUpdate = sent.Single(envelope => envelope.Type == MessageTypes.StateUpdate);
		var invoke = snapshot.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(invoke.Operation, Is.EqualTo(HostOperations.Ui.Snapshot));
			Assert.That(invoke.Arguments!.Value.GetProperty("sessionId").GetString(), Is.EqualTo("preview-1"));
			Assert.That(invoke.Arguments!.Value.GetProperty("tree").GetRawText(), Does.Contain("after"));
			Assert.That(invoke.Arguments!.Value.GetProperty("tree").GetRawText(), Does.Not.Contain("before"));
			Assert.That(stateUpdate.Payload!.Value.Deserialize<StateUpdatePayload>(PluginProtocolJson.Options)!.Kind,
				Is.EqualTo(CapabilityKinds.Ui));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_scenario_that_throws_on_hot_reload_faults_only_its_preview()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession());
		handler.DeclareCapabilities();

		await OpenPreviewAsync(handler, "preview-1");
		await OpenConfigAsync(handler, "config-1");

		HotReloadedViewPreviews.Throws = true;
		UiPreviewHotReload.UpdateApplication(null);

		var invokes = HostInvokes(await CollectAsync(socket, 3));

		Assert.That(invokes, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(invokes.Single(invoke => SessionOf(invoke) == "preview-1").Operation,
				Is.EqualTo(HostOperations.Ui.Fault));
			Assert.That(invokes.Single(invoke => SessionOf(invoke) == "config-1").Operation,
				Is.EqualTo(HostOperations.Ui.Reload));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_hot_reload_asks_the_host_to_reopen_every_open_real_session_and_leaves_dialogs_alone()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession());
		handler.DeclareCapabilities();

		await OpenConfigAsync(handler, "config-1");
		await OpenAsync(handler, "dialog-1", UiSurfaceKinds.Dialog);

		UiPreviewHotReload.UpdateApplication(null);

		var invokes = HostInvokes(await CollectAsync(socket, 2));

		Assert.That(invokes, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(invokes[0].Api, Is.EqualTo(HostApis.Ui));
			Assert.That(invokes[0].Operation, Is.EqualTo(HostOperations.Ui.Reload));
			Assert.That(SessionOf(invokes[0]), Is.EqualTo("config-1"));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_host_that_predates_reload_is_sent_a_fault_so_it_still_reopens_the_session()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession());

		await OpenConfigAsync(handler, "config-1");

		UiPreviewHotReload.UpdateApplication(null);

		var reload = await socket.NextAsync(MessageTypes.HostInvoke);
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-1",
			CorrelationId = reload.Id,
			Error = new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnsupported, Message = "unknown operation", Retryable = false
			}
		});

		var fault = HostInvokes([await socket.NextAsync(MessageTypes.HostInvoke)]).Single();

		Assert.Multiple(() =>
		{
			Assert.That(HostInvokes([reload]).Single().Operation, Is.EqualTo(HostOperations.Ui.Reload));
			Assert.That(fault.Operation, Is.EqualTo(HostOperations.Ui.Fault));
			Assert.That(SessionOf(fault), Is.EqualTo("config-1"));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_config_flow_serves_a_new_session_for_the_same_flow_after_a_reload()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		var flows = new PluginConfigFlowSessions(TimeProvider.System);
		var flow = new CountingUiConfigFlow();
		flows.Set("flow-1", flow);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession(), flows);

		Assert.That(await OpenIntegrationConfigAsync(handler, "config-1", "flow-1"), Is.True);

		UiPreviewHotReload.UpdateApplication(null);
		var reload = HostInvokes([await socket.NextAsync(MessageTypes.HostInvoke)]).Single();
		await handler.InvokeAsync(Close("config-1"), CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(reload.Operation, Is.EqualTo(HostOperations.Ui.Reload));
			Assert.That(await OpenIntegrationConfigAsync(handler, "config-2", "flow-1"), Is.True);
			Assert.That(flow.SessionsCreated, Is.EqualTo(2));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_plugin_that_declared_no_ui_capability_does_not_ask_for_a_new_describe()
	{
		var (hostInvoker, state, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);
		await using var handler = CreateHandler(hostInvoker, state, new ConfigSession());

		UiPreviewHotReload.UpdateApplication(null);

		await Assert.ThatAsync(() => socket.NextAsync(MessageTypes.StateUpdate, _quiet),
			Throws.InvalidOperationException);

		socket.CloseFromHost(1000);
		await run;
	}

	private static UiCapabilityHandler CreateHandler(IHostInvoker hostInvoker,
		PluginConnectionState state,
		ConfigSession configSession,
		PluginConfigFlowSessions? flows = null)
		=> new([new PreviewingIntegration(configSession)],
			hostInvoker,
			Serilog.Core.Logger.None,
			flows ?? new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore(),
			new PluginCatalogNotifier(state, Serilog.Core.Logger.None));

	private static async Task<UiSessionOpenResult> OpenPreviewAsync(UiCapabilityHandler handler, string sessionId)
	{
		var previewId = UiPreviewCatalog.Scan(typeof(HotReloadedViewPreviews).Assembly).Registrations
			.Single(registration => registration.Declaration.View == HotReloadedViewPreviews.View)
			.Declaration.Id;

		var result = await handler.InvokeAsync(Invocation(new UiSessionOpenArguments
			{
				SessionId = sessionId,
				SurfaceKind = UiSurfaceKinds.DeveloperPreview,
				SessionMode = UiSessionModes.Exclusive,
				UiModelVersion = 1,
				SurfaceAttributes = JsonSerializer.SerializeToElement(
					new Dictionary<string, string> { [UiDeveloperPreviewSurfaceAttributes.PreviewId] = previewId })
			}),
			CancellationToken.None);

		return result.Data!.Value.Deserialize<UiSessionOpenResult>(PluginProtocolJson.Options)!;
	}

	private static Task<CapabilityInvocationResult> OpenConfigAsync(UiCapabilityHandler handler, string sessionId)
		=> OpenAsync(handler, sessionId, UiSurfaceKinds.Config);

	private static Task<CapabilityInvocationResult> OpenAsync(UiCapabilityHandler handler, string sessionId,
		string surfaceKind)
		=> handler.InvokeAsync(Invocation(new UiSessionOpenArguments
			{
				SessionId = sessionId,
				SurfaceKind = surfaceKind,
				SessionMode = UiSessionModes.Exclusive,
				UiModelVersion = 1
			}),
			CancellationToken.None);

	private static async Task<bool> OpenIntegrationConfigAsync(UiCapabilityHandler handler, string sessionId,
		string flowId)
	{
		var result = await handler.InvokeAsync(Invocation(new UiSessionOpenArguments
			{
				SessionId = sessionId,
				SurfaceKind = UiSurfaceKinds.Config,
				SessionMode = UiSessionModes.Exclusive,
				UiModelVersion = 1,
				SurfaceAttributes = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					[UiConfigSurfaceAttributes.EntryPoint] = UiConfigEntryPoints.IntegrationConfig,
					[UiConfigSurfaceAttributes.ConfigFlowSessionId] = flowId
				})
			}),
			CancellationToken.None);

		return result.Data!.Value.Deserialize<UiSessionOpenResult>(PluginProtocolJson.Options)!.Accepted;
	}

	private static CapabilityInvocation Close(string sessionId)
		=> new()
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = CapabilityOperations.Ui.SessionClose,
			Arguments = JsonSerializer.SerializeToElement(new UiSessionCloseArguments { SessionId = sessionId },
				PluginProtocolJson.Options),
			CorrelationId = "c2",
			Services = new EmptyServiceProvider()
		};

	private static List<HostInvokePayload> HostInvokes(IEnumerable<ProtocolEnvelope> envelopes)
		=> envelopes.Where(envelope => envelope.Type == MessageTypes.HostInvoke)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!)
			.ToList();

	private static string? SessionOf(HostInvokePayload invoke)
		=> invoke.Arguments!.Value.GetProperty("sessionId").GetString();

	private static CapabilityInvocation Invocation(UiSessionOpenArguments arguments)
		=> new()
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = CapabilityOperations.Ui.SessionOpen,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "c1",
			Services = new EmptyServiceProvider()
		};

	private static async Task<List<ProtocolEnvelope>> CollectAsync(FakePluginSocket socket, int count)
	{
		var collected = new List<ProtocolEnvelope>();
		while (collected.Count < count)
		{
			collected.Add(await socket.NextAsync());
		}

		try
		{
			collected.Add(await socket.NextAsync(_quiet));
		}
		catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
		{
		}

		return collected;
	}

	private static (HostInvoker HostInvoker, PluginConnectionState State, FakePluginSocket Socket,
		Task<ConnectionOutcome> Run) Connect()
	{
		var state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(state, TimeProvider.System, Serilog.Core.Logger.None);

		var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			hostStateCache: null);

		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		return (hostInvoker, state, socket, connection.RunAsync(null, "instance-1", CancellationToken.None));
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	private sealed class CountingUiConfigFlow : IUiConfigFlow
	{
		public int SessionsCreated { get; private set; }

		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<ConfigFlowResult> SubmitAsync(string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			SessionsCreated++;
			return Task.FromResult<IUiSession?>(new ConfigSession());
		}
	}

	private sealed class ConfigSession : IUiSession
	{
		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted
		{
			add { }
			remove { }
		}

		public UiTree BuildTree() => new()
		{
			Revision = 1,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			Root = new UiNode { Id = "root", Type = "panel" }
		};

		public IReadOnlyList<UiPatch> DrainPatches() => [];

		public void Dispatch(UiEvent uiEvent)
		{
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
