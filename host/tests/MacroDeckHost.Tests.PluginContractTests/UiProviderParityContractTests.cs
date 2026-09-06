using System.Text;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The criterion the whole provider abstraction exists for: one session API, two provider kinds, and a
/// client that cannot tell which one it is talking to. Splitting this across two harnesses would not
/// establish it, so the same script runs twice and the two sets of observations are compared.
///
/// <para>
/// Run once per surface kind rather than once overall: routing is provider-id based and carries no
/// knowledge of the kind, but "the code looks generic" is not the same claim as "a plugin can serve it".
/// Every kind an opener produces is covered, so the answer to "can a plugin serve this surface" is never
/// again a reading of the routing code.
/// </para>
/// </summary>
[TestFixture]
internal sealed class UiProviderParityContractTests : UiContractFixture
{
	private const string InProcessProviderId = "com.example.inprocess";

	private static UiSurface Surface(string kind)
		=> new() { Kind = kind, SessionMode = Mode(kind) };

	/// <summary>The mode each kind is actually opened with, so a run is not parity against a surface no
	/// opener ever produces - a shared session admits a second client where an exclusive one refuses it,
	/// which is precisely the sort of difference this suite exists to catch.</summary>
	private static string Mode(string kind)
		=> kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview or UiSurfaceKinds.Folder
			? UiSessionModes.Shared
			: UiSessionModes.Exclusive;

	private static UiTree Tree(string kind)
		=> new() { Revision = 1, Surface = Surface(kind), Root = new UiNode { Id = "root", Type = "panel" } };

	private static readonly UiPatch _patch = new()
	{
		FromRevision = 1,
		ToRevision = 2,
		Operations =
		[
			new UiPatchOperation
			{
				Op = UiPatchOperations.InsertNode,
				NodeId = "field.apiKey",
				ParentId = "root",
				Node = new UiNode { Id = "field.apiKey", Type = "text-field" }
			}
		]
	};

	private static readonly byte[] _eventData = Encoding.UTF8.GetBytes("{\"value\":\"x\"}");

	[TestCase(UiSurfaceKinds.Config)]
	[TestCase(UiSurfaceKinds.Dialog)]
	[TestCase(UiSurfaceKinds.Widget)]
	[TestCase(UiSurfaceKinds.Preview)]
	[TestCase(UiSurfaceKinds.Folder)]
	public async Task The_same_session_script_produces_identical_client_observations_for_both_provider_kinds(
		string surfaceKind)
	{
		var inProcess = await RunInProcessAsync(surfaceKind);
		var outOfProcess = await RunOutOfProcessAsync(surfaceKind);

		Assert.Multiple(() =>
		{
			foreach (var run in new[] { inProcess, outOfProcess })
			{
				Assert.That(run.Attach.Accepted, Is.True, $"{run.Name}: the attach was refused.");
				Assert.That(run.Attach.Code, Is.Null, $"{run.Name}: an accepted attach carried a code.");
				Assert.That(run.Attach.SurfaceKind, Is.EqualTo(surfaceKind), run.Name);
				Assert.That(run.Attach.SessionMode, Is.EqualTo(Mode(surfaceKind)), run.Name);
				Assert.That(run.Attach.Revision, Is.EqualTo(1), run.Name);

				Assert.That(run.Messages.Select(message => message.GetType()),
					Is.EqualTo(new[]
					{
						typeof(UiSessionTreeUpdatedEvent), typeof(UiSessionPatchedEvent), typeof(UiSessionClosedEvent)
					}),
					$"{run.Name}: the client observed a different sequence of messages.");

				Assert.That(run.Messages.OfType<UiSessionTreeUpdatedEvent>().Single().Revision,
					Is.EqualTo(1),
					run.Name);
				Assert.That(run.Messages.OfType<UiSessionPatchedEvent>().Single().FromRevision,
					Is.EqualTo(1),
					run.Name);
				Assert.That(run.Messages.OfType<UiSessionPatchedEvent>().Single().ToRevision,
					Is.EqualTo(2),
					run.Name);
				Assert.That(run.Messages.OfType<UiSessionInvalidatedEvent>(), Is.Empty, run.Name);
			}

			Assert.That(outOfProcess.TreeBytes,
				Is.EqualTo(inProcess.TreeBytes),
				"The two provider kinds put different tree bytes in front of the same client.");
			Assert.That(outOfProcess.PatchBytes,
				Is.EqualTo(inProcess.PatchBytes),
				"The two provider kinds put different patch bytes in front of the same client.");

			Assert.That(inProcess.EventNodeId, Is.EqualTo(outOfProcess.EventNodeId));
			Assert.That(inProcess.EventName, Is.EqualTo(outOfProcess.EventName));
			Assert.That(inProcess.EventData, Is.EqualTo(_eventData));
			Assert.That(outOfProcess.EventData, Is.EqualTo(_eventData));
		});
	}

	private sealed record Run(
		string Name,
		UiAttachSessionResponse Attach,
		IReadOnlyList<object> Messages,
		byte[] TreeBytes,
		byte[] PatchBytes,
		string EventNodeId,
		string EventName,
		byte[] EventData);

	/// <summary>Run (a): an in-process provider, with no plugin session in existence at all.</summary>
	private async Task<Run> RunInProcessAsync(string surfaceKind)
	{
		var transport = new RecordingUiTransport();
		using var registry = new UiSessionRegistry(Time);
		var integrations = new StubIntegrationRegistry();
		var pluginSessions = new PluginSessionRegistry(Time, Serilog.Core.Logger.None);
		var session = new InProcessUiTestSession { Tree = Tree(surfaceKind) };
		integrations.Add(new InProcessUiTestIntegration(InProcessProviderId, session, surfaceKind));

		UiSessionBroker? broker = null;
		var resolver = new UiSessionProviderResolver(new RemoteUiProviderRegistry(new InMemorySnapshotStore(), Invoker),
			new UiProviderRegistry(integrations, () => broker!, Serilog.Core.Logger.None),
			new ConfigFlowUiProviderRegistry(() => broker!, Serilog.Core.Logger.None),
			new ActionConfigUiProviderRegistry(integrations, () => broker!, Serilog.Core.Logger.None),
			new WidgetUiProviderRegistry(new EmptyFolderCache(), [], () => broker!, Serilog.Core.Logger.None),
			new IntegrationUiProviderRegistry([], () => Broker, Serilog.Core.Logger.None),
			new UiPreviewProviderRegistry([], () => Broker, Serilog.Core.Logger.None));

		broker = new UiSessionBroker(resolver,
			transport,
			registry,
			pluginSessions,
			integrations,
			Time,
			Serilog.Core.Logger.None);

		using (broker)
		{
			Assert.That(pluginSessions.Snapshot(), Is.Empty, "The in-process run needed a plugin session.");

			var ticket = await broker.OpenAsync(InProcessProviderId,
				Surface(surfaceKind),
				OwnerPrincipal,
				CancellationToken.None);
			var attach = broker.Attach(ticket.SessionId, "c1", OwnerPrincipal);
			await WaitForUiAsync(() => transport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
				"The in-process tree never reached the client.");

			session.Emit(_patch);
			await WaitForUiAsync(() => transport.MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
				"The in-process patch never reached the client.");

			broker.SendEvent(EventRequest(ticket.SessionId), "c1");
			await WaitForUiAsync(() => session.Dispatched.Count == 1,
				"The event never reached the in-process provider.");

			await broker.CloseAsync(ticket.SessionId, "done", CancellationToken.None);
			await WaitForUiAsync(() => transport.MessagesFor<UiSessionClosedEvent>("c1").Count == 1,
				"The in-process session never told the client it closed.");
			await SettleAsync();

			var dispatched = session.Dispatched.Single();

			return new Run("in-process",
				attach,
				[.. transport.For("c1").Select(recorded => recorded.Message)],
				PayloadOf<UiSessionTreeUpdatedEvent>(transport),
				PayloadOf<UiSessionPatchedEvent>(transport),
				dispatched.NodeId,
				dispatched.Name,
				Encoding.UTF8.GetBytes(dispatched.Data!.Value.GetRawText()));
		}
	}

	/// <summary>Run (b): the same logic behind the plugin link, declaring kind <c>ui</c>.</summary>
	private async Task<Run> RunOutOfProcessAsync(string surfaceKind)
	{
		var treeJson = UiCanonicalJson.Serialize(Tree(surfaceKind));
		var patchJson = UiCanonicalJson.Serialize(_patch);

		var handler = new TestUiCapabilityHandler
		{
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, treeJson))
		};

		await ConnectAsync([handler],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Ui,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.Ui]);

		var ticket = await Broker.OpenAsync(PluginId, Surface(surfaceKind), OwnerPrincipal, CancellationToken.None);
		var attach = Attach(ticket.SessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The plugin's tree never reached the client.");

		await Link.SendRawFromPluginAsync(UiWire.Patch(ticket.SessionId, patchJson));
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The plugin's patch never reached the client.");

		Broker.SendEvent(EventRequest(ticket.SessionId), "c1");
		await WaitForUiAsync(() => handler.Events.Count == 1, "The event never reached the plugin.");

		await Broker.CloseAsync(ticket.SessionId, "done", CancellationToken.None);
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionClosedEvent>("c1").Count == 1,
			"The plugin-served session never told the client it closed.");
		await SettleAsync();

		var dispatched = handler.Events.Single();

		return new Run("out-of-process",
			attach,
			[.. UiTransport.For("c1").Select(recorded => recorded.Message)],
			PayloadOf<UiSessionTreeUpdatedEvent>(UiTransport),
			PayloadOf<UiSessionPatchedEvent>(UiTransport),
			dispatched.NodeId,
			dispatched.Name,
			Encoding.UTF8.GetBytes(dispatched.Data!.Value.GetRawText()));
	}

	private static UiSendEventRequest EventRequest(string sessionId)
		=> new()
		{
			SessionId = sessionId,
			NodeId = "field.apiKey",
			Name = "input",
			Data = new UiRawJson(_eventData),
			Revision = 1
		};

	private static byte[] PayloadOf<T>(RecordingUiTransport transport)
		where T : class
		=> transport.For("c1").Single(recorded => recorded.Message is T).Payload!;
}
