using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// The plugin end of the <c>ui</c> capability. The guarantee under test is the same one the host proves
/// for the other direction: whatever a provider produces reaches the peer as the bytes it produced, so a
/// tree or patch is serialized exactly once and relayed, never re-encoded on the way to the socket.
/// </summary>
[TestFixture]
public class UiCapabilityHandlerTests
{
	// Verbatim, and every character load-bearing - the same construction the host's own byte-fidelity
	// contract tests use: a literal e-acute escape, an unescaped '<', two consecutive spaces before
	// "toRevision", a duplicate "a" member, "z" before "a", the envelope members in reverse order, an
	// unmapped "x-vendor" member and a 39-digit integer.
	private const string AdversarialPatch =
		"{\"operations\":[{\"op\":\"set-properties\",\"nodeId\":\"field.apiKey\",\"properties\":" +
		"{\"z\":1.0,\"a\":\"caf\\u00e9 a<b\",\"a\":\"shadowed\"," +
		"\"n\":100000000000000000000000000000000000001}}],  " +
		"\"toRevision\":2,\"fromRevision\":1,\"x-vendor\":{\"keep\":true}}";

	[Test]
	public async Task A_patch_a_provider_authored_reaches_the_host_as_the_exact_bytes_it_produced()
	{
		var (hostInvoker, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);

		using var document = JsonDocument.Parse(Encoding.UTF8.GetBytes(AdversarialPatch));

		var invoke = hostInvoker.InvokeAsync(HostApis.Ui,
			HostOperations.Ui.Patch,
			new UiPatchArguments { SessionId = "s1", Patch = document.RootElement },
			CancellationToken.None);

		var sent = await socket.NextAsync(MessageTypes.HostInvoke);

		AssertVerbatim(AdversarialPatch, PatchOf(sent));

		socket.CloseFromHost(1000);
		await run;
		await SwallowAsync(invoke);
	}

	[Test]
	public async Task The_handler_serializes_a_patch_once_and_sends_those_bytes_unchanged()
	{
		var session = new FakeUiSession();
		var (hostInvoker, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);

		await using var handler = new UiCapabilityHandler([new FakeUiIntegration(session)],
			hostInvoker,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await OpenAsync(handler, "s1");

		var patch = new UiPatch
		{
			FromRevision = 1,
			ToRevision = 2,
			Operations =
			[
				new UiPatchOperation
				{
					Op = UiPatchOperations.SetProperties, NodeId = "root", Properties = Properties()
				}
			]
		};

		session.Emit(patch);

		var sent = await socket.NextAsync(MessageTypes.HostInvoke);

		Assert.That(PatchOf(sent),
			Is.EqualTo(UiCanonicalJson.SerializeToUtf8Bytes(patch)),
			"The patch was re-encoded between the canonical serializer and the socket.");

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_snapshot_request_is_answered_by_a_pushed_tree_rather_than_by_the_result()
	{
		var session = new FakeUiSession();
		var (hostInvoker, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);

		await using var handler = new UiCapabilityHandler([new FakeUiIntegration(session)],
			hostInvoker,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await OpenAsync(handler, "s1");

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionSnapshot,
				new UiSessionSnapshotArguments { SessionId = "s1" }),
			CancellationToken.None);

		var sent = await socket.NextAsync(MessageTypes.HostInvoke);
		var payload = sent.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(result.Data, Is.Null, "The tree came back on the result instead of being pushed.");
			Assert.That(payload.Operation, Is.EqualTo(HostOperations.Ui.Snapshot));
			Assert.That(Encoding.UTF8.GetBytes(payload.Arguments!.Value.GetProperty("tree").GetRawText()),
				Is.EqualTo(UiCanonicalJson.SerializeToUtf8Bytes(session.Tree)));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task A_session_the_provider_faults_is_reported_to_the_host()
	{
		var session = new FakeUiSession();
		var (hostInvoker, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);

		await using var handler = new UiCapabilityHandler([new FakeUiIntegration(session)],
			hostInvoker,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await OpenAsync(handler, "s1");

		session.Fault();

		var sent = await socket.NextAsync(MessageTypes.HostInvoke);
		var payload = sent.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload.Api, Is.EqualTo(HostApis.Ui));
			Assert.That(payload.Operation, Is.EqualTo(HostOperations.Ui.Fault));
			Assert.That(payload.Arguments!.Value.GetProperty("sessionId").GetString(), Is.EqualTo("s1"));
		});

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task Closing_a_session_disposes_the_provider_session()
	{
		var session = new FakeUiSession();
		var (hostInvoker, socket, run) = Connect();
		await socket.NextAsync(MessageTypes.SessionHello);

		await using var handler = new UiCapabilityHandler([new FakeUiIntegration(session)],
			hostInvoker,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await OpenAsync(handler, "s1");

		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionClose,
				new UiSessionCloseArguments { SessionId = "s1" }),
			CancellationToken.None);

		Assert.That(session.DisposeCalls, Is.EqualTo(1));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task Describe_reports_the_surfaces_the_providers_declared()
	{
		await using var handler = new UiCapabilityHandler([new FakeUiIntegration(new FakeUiSession())],
			new NoOpHostInvoker(),
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.Describe, arguments: null),
			CancellationToken.None);

		var described = result.Data!.Value.Deserialize<UiDescribePayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(handler.DeclareCapabilities().Single().LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(described.Surfaces.Single().Kind, Is.EqualTo(UiSurfaceKinds.Config));
			Assert.That(described.Surfaces.Single().SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(described.UiModelVersion, Is.GreaterThan(0));
		});
	}

	[Test]
	public async Task A_plugin_with_no_ui_provider_declares_no_ui_capability()
	{
		await using var handler = new UiCapabilityHandler([],
			new NoOpHostInvoker(),
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		Assert.That(handler.DeclareCapabilities(), Is.Empty);
	}

	/// <summary>Full byte equality plus one assertion per transform, so a failure names the round trip
	/// that crept in rather than only reporting that one did.</summary>
	private static void AssertVerbatim(string expected, byte[] delivered)
	{
		var text = Encoding.UTF8.GetString(delivered);

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.EqualTo(Encoding.UTF8.GetBytes(expected)));

			Assert.That(text,
				Does.Contain("caf\\u00e9"),
				"The literal escape was unescaped: a reader/writer pass emitted the UTF-8 bytes of e-acute.");
			Assert.That(text,
				Does.Contain("a<b"),
				"'<' was re-escaped: JsonElement.WriteTo emits a\\u003Cb.");
			Assert.That(text,
				Does.Contain("  \"toRevision\""),
				"Insignificant whitespace was collapsed: any reader/writer pass drops it.");
			Assert.That(text.Split("\"a\":").Length - 1,
				Is.EqualTo(2),
				"A duplicate member was collapsed: binding to a dictionary keeps only the last.");
			Assert.That(text.IndexOf("\"z\":", StringComparison.Ordinal),
				Is.LessThan(text.IndexOf("\"a\":", StringComparison.Ordinal)),
				"Map keys were sorted: UiCanonicalJson orders them ordinal ascending.");
			Assert.That(text.IndexOf("\"toRevision\"", StringComparison.Ordinal),
				Is.LessThan(text.IndexOf("\"fromRevision\"", StringComparison.Ordinal)),
				"Envelope members were reordered: re-serializing the model restores [JsonPropertyOrder].");
			Assert.That(text,
				Does.Contain("\"x-vendor\""),
				"An unmapped member was dropped: UnmappedMemberHandling.Skip discards it.");
			Assert.That(text,
				Does.Contain("100000000000000000000000000000000000001"),
				"A 39-digit integer lost digits: a double hop collapses it.");
		});
	}

	private static byte[] PatchOf(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!;

		return Encoding.UTF8.GetBytes(payload.Arguments!.Value.GetProperty("patch").GetRawText());
	}

	private static Task<CapabilityInvocationResult> OpenAsync(UiCapabilityHandler handler, string sessionId)
		=> handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = sessionId,
					SurfaceKind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					UiModelVersion = 1
				}),
			CancellationToken.None);

	private static CapabilityInvocation Invocation(string operation, object? arguments)
		=> new()
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "c1",
			Services = new EmptyServiceProvider()
		};

	private static Dictionary<string, JsonElement> Properties()
		=> new(StringComparer.Ordinal)
		{
			["title"] = JsonSerializer.SerializeToElement("x")
		};

	private static async Task SwallowAsync(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// The host in this test never answers the invocation; the connection ending fails it, which
			// is the behaviour HostInvokerTests covers and not what is under test here.
		}
	}

	private static (HostInvoker HostInvoker, FakePluginSocket Socket, Task<ConnectionOutcome> Run) Connect()
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

		return (hostInvoker, socket, connection.RunAsync(null, "instance-1", CancellationToken.None));
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	private sealed class NoOpHostInvoker : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
			=> Task.FromResult<JsonElement?>(null);

		public bool TryComplete(ProtocolEnvelope result) => false;
	}

	private sealed class FakeUiIntegration : IPluginIntegration, IUiProvider
	{
		private readonly FakeUiSession _session;

		public FakeUiIntegration(FakeUiSession session) => _session = session;

		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
		[
			new()
				{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
		];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult<IUiSession?>(_session);
	}

	private sealed class FakeUiSession : IUiSession
	{
		private readonly Lock _gate = new();
		private readonly List<UiPatch> _pending = [];

		public UiTree Tree { get; } = new()
		{
			Revision = 1,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			Root = new UiNode { Id = "root", Type = "panel" }
		};

		public List<UiEvent> Dispatched { get; } = [];

		public int DisposeCalls { get; private set; }

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree() => Tree;

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			lock (_gate)
			{
				var drained = _pending.ToArray();
				_pending.Clear();
				return drained;
			}
		}

		public void Dispatch(UiEvent uiEvent)
		{
			lock (_gate)
			{
				Dispatched.Add(uiEvent);
			}
		}

		public void Emit(UiPatch patch)
		{
			lock (_gate)
			{
				_pending.Add(patch);
			}

			Changed?.Invoke(this, EventArgs.Empty);
		}

		public void Fault() => Faulted?.Invoke(this, new UiSessionFaultedEventArgs("gone"));

		public ValueTask DisposeAsync()
		{
			DisposeCalls++;
			return ValueTask.CompletedTask;
		}
	}
}
