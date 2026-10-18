using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Links;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class InProcessUiEventOriginTests : UiSessionFixture
{
	private const string IntegrationId = "app.example.widgets";
	private const string ClientId = "client-a";
	private const string Connection = "conn-a";
	private static readonly Guid _widgetId = Guid.Parse("0b6f3a2e-7c4d-4e8b-9a1f-5d2c6e8b4a70");

	private OriginSession _session = null!;

	[TearDown]
	public async Task DisposeSession()
	{
		if (_session is not null)
		{
			await _session.DisposeAsync();
		}
	}

	private async Task<string> PressAsync()
	{
		_session = new OriginSession();
		Integrations.Add(new OriginIntegration(_session));
		var ticket = await Broker.OpenAsync(IntegrationId,
			new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString())
				}
			},
			DeviceA,
			CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True);
		Attach(ticket.SessionId, Connection);

		Broker.SendEvent(new UiSendEventRequest
				{ SessionId = ticket.SessionId, NodeId = "root", Name = UiComponentEvents.Press },
			Connection,
			ClientId);
		await WaitForAsync(() => _session.Origin is not null, "The press never reached the session.");
		return ticket.SessionId;
	}

	private IReadOnlyList<ActionExecutionStatusEvent> FailuresShownToTheClient()
	{
		return
		[
			.. Transport.InGroup(UiClientGroups.For(ClientId))
				.Select(recorded => recorded.Message)
				.OfType<ActionExecutionStatusEvent>()
		];
	}

	[Test]
	public async Task A_failure_reported_after_the_handler_returned_reaches_the_pressing_client()
	{
		await PressAsync();

		var delivered = await _session.Origin!.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));

		var failure = FailuresShownToTheClient().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_session.Origin.ClientId, Is.EqualTo(ClientId));
			Assert.That(delivered, Is.True);
			Assert.That(failure.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(failure.Error!.Code, Is.EqualTo(ActionExecutionErrorCodes.ActionFailed));
			Assert.That(failure.WidgetId, Is.EqualTo(_widgetId.ToString()));
		});
	}

	[Test]
	public async Task The_pressing_client_is_shown_one_of_the_integrations_dialogs()
	{
		await PressAsync();

		var opened = await _session.Origin!.ShowModalAsync(new ModalDefinition { ViewId = "details" });

		Assert.Multiple(() =>
		{
			Assert.That(opened, Is.True);
			Assert.That(Interactions.Opened.Single().IntegrationId, Is.EqualTo(IntegrationId));
			Assert.That(Interactions.Opened.Single().ClientId, Is.EqualTo(ClientId));
		});
	}

	[Test]
	public async Task The_pressing_connection_is_sent_a_link_it_said_it_can_open()
	{
		ClientFeatures.Declare(Connection, [UiClientFeatures.OpenUrl]);
		await PressAsync();

		var delivered = await _session.Origin!.OpenUrlAsync(new Uri("https://example.com/article"));

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.True);
			Assert.That(MessagesFor<UiOpenUrlEvent>(Connection).Single().Url,
				Is.EqualTo("https://example.com/article"));
			Assert.ThrowsAsync<ArgumentException>(() => _session.Origin.OpenUrlAsync(new Uri("javascript:alert(1)")));
		});
	}

	[Test]
	public async Task A_client_that_detached_is_no_longer_reached()
	{
		var sessionId = await PressAsync();
		Broker.Detach(sessionId, Connection);

		var delivered = await _session.Origin!.ReportFailureAsync(LocalizedText.FromLiteral("Too late."));
		var opened = await _session.Origin.ShowModalAsync(new ModalDefinition { ViewId = "details" });
		var answer = await _session.Origin.ShowModalAsync<string>(new ModalDefinition { ViewId = "details" });

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.False);
			Assert.That(opened, Is.False);
			Assert.That(answer.Cancelled, Is.True);
			Assert.That(FailuresShownToTheClient(), Is.Empty);
			Assert.That(Interactions.Opened, Is.Empty);
		});
	}

	private sealed class OriginIntegration(OriginSession session) : IIntegration, IUiProvider
	{
		public string Id => IntegrationId;

		public LocalizedText Name => "Widgets";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

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
			return Task.FromResult<IUiSession?>(session);
		}
	}

	private sealed class OriginSession : IUiSession
	{
		private volatile IUiEventOrigin? _origin;

		public IUiEventOrigin? Origin => _origin;

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
				Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				Root = new UiNode { Id = "root", Type = UiComponents.Stack }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
		}

		public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
		{
			_origin = origin;
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
