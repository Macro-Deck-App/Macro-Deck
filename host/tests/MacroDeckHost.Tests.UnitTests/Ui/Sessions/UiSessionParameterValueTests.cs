using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionParameterValueTests : UiSessionFixture
{
	private const string IntegrationId = "com.example.compose";

	[Test]
	public async Task A_flush_sent_right_after_an_edit_reaches_the_client_after_the_value_that_edit_reported()
	{
		Integrations.Add(new ComposeIntegration());
		var sessionId = await OpenActionConfigAsync();
		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "content", Name = "change" }, "c1");
		var flush = Broker.FlushSession(new FlushUiSessionRequest { SessionId = sessionId, FlushId = "f1" }, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionFlushedEvent>("c1").Count == 1,
			"The flush was never answered.");

		var messages = MessagesFor("c1");
		var reported = messages.OfType<UiSessionParameterValuesReportedEvent>().Single();
		var flushed = messages.OfType<UiSessionFlushedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(flush.Accepted, Is.True);
			Assert.That(messages.ToList().IndexOf(reported),
				Is.LessThan(messages.ToList().IndexOf(flushed)),
				"The flush overtook the value the edit before it reported.");
			Assert.That(flushed.FlushId, Is.EqualTo("f1"));
			Assert.That(reported.SessionId, Is.EqualTo(sessionId));
		});
	}

	[Test]
	public async Task Only_declared_values_of_the_right_shape_that_are_not_secret_reach_the_client()
	{
		Integrations.Add(new ComposeIntegration());
		var sessionId = await OpenActionConfigAsync();
		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "content", Name = "change" }, "c1");
		Broker.FlushSession(new FlushUiSessionRequest { SessionId = sessionId, FlushId = "f1" }, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionFlushedEvent>("c1").Count == 1,
			"The flush was never answered.");

		var values = MessagesFor<UiSessionParameterValuesReportedEvent>("c1").Single().Values;
		Assert.That(string.Join(",", values.Keys.Order(StringComparer.Ordinal)), Is.EqualTo("message,title"));
	}

	[Test]
	public async Task Values_reported_for_a_session_that_configures_no_action_are_ignored()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		var result = Broker.PublishParameterValues(ProviderId,
			sessionId,
			new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("x") });
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.True);
			Assert.That(MessagesFor<UiSessionParameterValuesReportedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task A_connection_not_attached_to_the_session_cannot_flush_it()
	{
		Integrations.Add(new ComposeIntegration());
		var sessionId = await OpenActionConfigAsync();
		Attach(sessionId, "c1");

		var flush = Broker.FlushSession(new FlushUiSessionRequest { SessionId = sessionId, FlushId = "f1" }, "c2");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(flush.Accepted, Is.False);
			Assert.That(flush.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(MessagesFor<UiSessionFlushedEvent>("c2"), Is.Empty);
		});
	}

	private async Task<string> OpenActionConfigAsync()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.ActionConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(IntegrationId),
				[UiConfigSurfaceAttributes.ActionId] = JsonSerializer.SerializeToElement(ComposeAction.ActionId)
			}
		};

		var ticket = await Broker.OpenAsync(ActionConfigUiProviderRegistry.ProviderIdFor(IntegrationId,
				ComposeAction.ActionId),
			surface,
			DeviceA,
			CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		return ticket.SessionId;
	}

	private sealed class ComposeIntegration : IIntegration
	{
		public string Id => IntegrationId;

		public LocalizedText Name => "Compose";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [new ComposeAction()];

		public bool IsInitialized => true;

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
				{ Name = "title", Type = ActionParameterType.String },
			new()
				{ Name = "count", Type = ActionParameterType.Number },
			new()
				{ Name = "token", Type = ActionParameterType.Secret },
			new()
				{ Name = "password", Type = ActionParameterType.Password },
			new()
				{ Name = "headers", Type = ActionParameterType.KeyValue }
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
				["title"] = JsonSerializer.SerializeToElement("Title"),
				["count"] = JsonSerializer.SerializeToElement(new { value = 3 }),
				["token"] = JsonSerializer.SerializeToElement("secret-token"),
				["password"] = JsonSerializer.SerializeToElement("hunter2"),
				["headers"] = JsonSerializer.SerializeToElement(new { retries = 3 }),
				["undeclared"] = JsonSerializer.SerializeToElement("x")
			});
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
