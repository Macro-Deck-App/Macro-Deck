using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome.Configuration;
using MacroDeckHost.Integrations.AdGuardHome.State;
using MacroDeckHost.Integrations.AdGuardHome.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeWidgetHostTests : UiSessionFixture
{
	private const string ClientId = "client-a";
	private const string Connection = "conn-a";
	private static readonly Guid _widgetId = Guid.Parse("0b6f4c31-6a2e-4d1b-9f27-3e5a8c9d1f40");

	private FakeAdGuardHomeClient _client = null!;
	private AdGuardHomeIntegration _integration = null!;
	private Guid _entryId;

	[SetUp]
	public async Task SetUpIntegration()
	{
		_client = new FakeAdGuardHomeClient();
		_integration = new AdGuardHomeIntegration(_ => _client, TimeProvider.System);
		var context = new FakeAdGuardHomeContext();
		_entryId = context.AddEntry("Home",
			new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });
		await _integration.InitializeAsync(context);
		await CalendarTree.WaitForAsync(() => _integration.Instances.Instances is [{ IsConnected: true }],
			"the instance never connected");
		Integrations.Add(_integration);
	}

	[TearDown]
	public Task TearDownIntegration()
	{
		return _integration.ShutdownAsync();
	}

	[Test]
	public async Task A_press_while_the_host_is_locked_is_refused_before_it_reaches_the_server()
	{
		var (sessionId, pauseNode) = await OpenWidgetAsync();
		HostLock.IsLocked = true;

		var response = Press(sessionId, pauseNode);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.HostLocked));
			Assert.That(_client.ProtectionCalls, Is.Empty);
			Assert.That(StatusEventsToClient().Single().Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(StatusEventsToClient().Single().WidgetId, Is.EqualTo(_widgetId.ToString()));
		});
	}

	[Test]
	public async Task A_failed_command_is_reported_to_the_client_that_pressed()
	{
		var (sessionId, pauseNode) = await OpenWidgetAsync();
		_client.Failure = AdGuardHomeConnection.Unauthorized;

		var response = Press(sessionId, pauseNode);
		await WaitForAsync(() => StatusEventsToClient().Count == 1, "the failure never reached the client");

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(StatusEventsToClient().Single().Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(StatusEventsToClient().Single().Error?.Code,
				Is.EqualTo(ActionExecutionErrorCodes.ActionFailed));
			Assert.That(StatusEventsToClient().Single().WidgetId, Is.EqualTo(_widgetId.ToString()));
		});
	}

	private async Task<(string SessionId, string PauseNode)> OpenWidgetAsync()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString()),
				[UiWidgetSurfaceAttributes.WidgetType]
					= JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId),
				[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new
				{
					instance = _entryId.ToString("D"),
					durations = new[] { "5m" }
				})
			}
		};

		await using (var probe = (await _integration.CreateSessionAsync(
			new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None))!)
		{
			var pauseNode = CalendarTree.Flatten(probe.BuildTree().Root)
				.First(node
					=> node.Id.EndsWith(".pause-5m", StringComparison.Ordinal) && CalendarTree.IsPressable(node))
				.Id;

			var ticket = await Broker.OpenAsync(AdGuardHomeIntegration.IntegrationId,
				surface,
				DeviceA,
				CancellationToken.None);
			Assert.That(ticket.Accepted, Is.True, "The widget session did not open.");
			Assert.That(Attach(ticket.SessionId, Connection).Accepted, Is.True);
			return (ticket.SessionId, pauseNode);
		}
	}

	private UiSendEventResponse Press(string sessionId, string nodeId)
	{
		return Broker.SendEvent(new UiSendEventRequest
				{ SessionId = sessionId, NodeId = nodeId, Name = UiComponentEvents.Press },
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
}
