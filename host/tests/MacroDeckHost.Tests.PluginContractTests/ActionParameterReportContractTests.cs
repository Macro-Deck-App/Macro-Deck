using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class ActionParameterReportContractTests : UiContractFixture
{
	private const string PlainTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\",\"attributes\":{}},"
		+ "\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	[Test]
	public async Task A_flush_sent_right_after_an_edit_reaches_the_client_after_the_value_the_plugin_reported()
	{
		var integration = new ComposeIntegration();
		await ConnectAsync([
				new ActionsCapabilityHandler([integration]),
				new UiCapabilityHandler([integration],
					CreatePluginHostInvoker(),
					Serilog.Core.Logger.None,
					new PluginConfigFlowSessions(TimeProvider.System),
					new ModalResultStore())
			],
			[
				Declared(CapabilityKinds.Actions, ComposeAction.ActionId),
				Declared(CapabilityKinds.Ui, ProviderCapabilityId.LocalId)
			],
			[CapabilityKinds.Actions, CapabilityKinds.Ui]);

		var sessionId = await OpenAsync(ActionConfigSurface());
		Attach(sessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "content", Name = "change" }, "c1");
		Broker.FlushSession(new FlushUiSessionRequest { SessionId = sessionId, FlushId = "f1" }, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionFlushedEvent>("c1").Count == 1,
			"The flush was never answered.");

		var messages = UiTransport.For("c1").Select(recorded => recorded.Message).ToList();
		var reported = messages.OfType<UiSessionParameterValuesReportedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(messages.IndexOf(reported),
				Is.LessThan(messages.IndexOf(messages.OfType<UiSessionFlushedEvent>().Single())),
				"The flush overtook the value the edit before it reported.");
			Assert.That(reported.Values.Keys, Is.EquivalentTo((string[])["message"]));
			Assert.That(reported.Values["message"].GetProperty("content").GetString(), Is.EqualTo("hi"));
		});
	}

	[Test]
	public async Task A_plugin_built_before_flush_existed_is_treated_as_flushed()
	{
		var handler = new TestUiCapabilityHandler
		{
			SupportsFlush = false,
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, PlainTree))
		};
		await ConnectAsync([handler],
			[Declared(CapabilityKinds.Ui, ProviderCapabilityId.LocalId)],
			[CapabilityKinds.Ui]);

		var sessionId = await OpenAsync(new UiSurface
		{
			Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive
		});
		Attach(sessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		var flush = Broker.FlushSession(new FlushUiSessionRequest { SessionId = sessionId, FlushId = "f1" }, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionFlushedEvent>("c1").Count == 1,
			"A plugin that does not know session.flush left the flush unanswered.");

		Assert.Multiple(() =>
		{
			Assert.That(flush.Accepted, Is.True);
			Assert.That(UiTransport.MessagesFor<UiSessionInvalidatedEvent>("c1"),
				Is.Empty,
				"An unsupported flush faulted the session.");
		});
	}

	private static DeclaredCapability Declared(string kind, string localId)
	{
		return new DeclaredCapability
		{
			Kind = kind, LocalId = localId, VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};
	}

	private static UiSurface ActionConfigSurface()
	{
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.ActionConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(PluginId),
				[UiConfigSurfaceAttributes.ActionId] = JsonSerializer.SerializeToElement(ComposeAction.ActionId)
			}
		};
	}

	private async Task<string> OpenAsync(UiSurface surface)
	{
		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		return ticket.SessionId;
	}

	private sealed class ComposeIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [new ComposeAction()];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	private sealed class ComposeAction : IUiConfigurableActionDefinition
	{
		public const string ActionId = "compose";

		public string Id => ActionId;

		public LocalizedText Name => "Compose";

		public LocalizedText Description => "Composes a message.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			new()
				{ Name = "message", Type = ActionParameterType.KeyValue },
			new()
				{ Name = "token", Type = ActionParameterType.Secret }
		];

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<IUiSession?> CreateConfigurationSessionAsync(
			ActionConfigurationRequest request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(new ComposeSession().Configuration);
		}
	}

	private sealed class ComposeSession : IUiSession
	{
		public ComposeSession()
		{
			Configuration = new ActionConfigurationSession(this);
		}

		public ActionConfigurationSession Configuration { get; }

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

		public UiTree BuildTree()
		{
			return new UiTree
			{
				Revision = 1,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				Root = new UiNode { Id = "root", Type = "panel" }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Configuration.Report(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				["message"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["content"] = "hi" }),
				["token"] = JsonSerializer.SerializeToElement("secret-token")
			});
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
