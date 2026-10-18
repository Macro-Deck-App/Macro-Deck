using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Links;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class UiEventOriginContractTests : UiContractFixture
{
	private const string ClientId = "client-a";
	private const string Connection = "conn-a";
	private static readonly Guid _widgetId = Guid.Parse("8e8c6a0c-1f2b-4d1a-9a55-2b8f3c1d7e60");

	private const string WidgetTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"widget\",\"sessionMode\":\"shared\",\"attributes\":{}},"
		+ "\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	private TestUiCapabilityHandler _handler = null!;

	private async Task<string> OpenPressedWidgetAsync()
	{
		_handler = new TestUiCapabilityHandler
		{
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, WidgetTree))
		};

		await ConnectAsync([_handler],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Ui,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.Ui]);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString())
			}
		};
		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, "The plugin's widget session did not open.");
		Assert.That(Attach(ticket.SessionId, Connection).Accepted, Is.True);

		Broker.SendEvent(new UiSendEventRequest
				{ SessionId = ticket.SessionId, NodeId = "root", Name = UiComponentEvents.Press },
			Connection,
			ClientId);
		await WaitForUiAsync(() => _handler.Events.Count == 1, "The press never reached the plugin.");
		return ticket.SessionId;
	}

	private Task<HostCallbackResult> InvokeAsync(string operation, object arguments)
	{
		return HostInvokeHandler!(Guid.NewGuid().ToString("N"),
			new HostInvokePayload
			{
				Api = HostApis.Ui,
				Operation = operation,
				Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
			},
			CancellationToken.None);
	}

	private IReadOnlyList<ActionExecutionStatusEvent> FailuresShownToTheClient()
	{
		return
		[
			.. UiTransport.All.Where(recorded => recorded.Group == UiClientGroups.For(ClientId))
				.Select(recorded => recorded.Message)
				.OfType<ActionExecutionStatusEvent>()
		];
	}

	[Test]
	public async Task The_plugin_learns_which_client_pressed_and_can_show_it_a_failure()
	{
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginReportFailure,
			new UiOriginReportFailureArguments
			{
				SessionId = sessionId,
				ClientId = _handler.Events.Single().ClientId!,
				EventName = UiComponentEvents.Press,
				Message = LocalizedText.FromLiteral("The server did not answer.")
			});

		var failure = FailuresShownToTheClient().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_handler.Events.Single().ClientId, Is.EqualTo(ClientId));
			Assert.That(result.Data!.Value.Deserialize<UiOriginReportFailureResult>(PluginProtocolJson.Options)!
					.Delivered,
				Is.True);
			Assert.That(failure.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(failure.WidgetId, Is.EqualTo(_widgetId.ToString()));
			Assert.That(failure.Error!.Message.Literal, Is.EqualTo("The server did not answer."));
		});
	}

	[Test]
	public async Task A_client_that_detached_is_no_longer_reached()
	{
		var sessionId = await OpenPressedWidgetAsync();
		Broker.Detach(sessionId, Connection);

		var failure = await InvokeAsync(HostOperations.Ui.OriginReportFailure,
			new UiOriginReportFailureArguments
			{
				SessionId = sessionId, ClientId = ClientId, Message = LocalizedText.FromLiteral("Too late.")
			});
		var modal = await InvokeAsync(HostOperations.Ui.OriginShowModal,
			new UiOriginShowModalArguments { SessionId = sessionId, ClientId = ClientId, ViewId = "details" });

		Assert.Multiple(() =>
		{
			Assert.That(failure.Data!.Value.Deserialize<UiOriginReportFailureResult>(PluginProtocolJson.Options)!
					.Delivered,
				Is.False);
			Assert.That(modal.Data!.Value.Deserialize<UiOriginShowModalResult>(PluginProtocolJson.Options)!.Opened,
				Is.False);
			Assert.That(FailuresShownToTheClient(), Is.Empty);
		});
	}

	[Test]
	public async Task A_client_that_never_pressed_in_the_session_is_not_reached()
	{
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginReportFailure,
			new UiOriginReportFailureArguments
			{
				SessionId = sessionId, ClientId = "someone-else", Message = LocalizedText.FromLiteral("Hello.")
			});

		Assert.That(result.Data!.Value.Deserialize<UiOriginReportFailureResult>(PluginProtocolJson.Options)!.Delivered,
			Is.False);
	}

	[Test]
	public async Task The_pressing_client_is_shown_one_of_the_plugins_dialogs()
	{
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginShowModal,
			new UiOriginShowModalArguments { SessionId = sessionId, ClientId = ClientId, ViewId = "details" });

		var opened = result.Data!.Value.Deserialize<UiOriginShowModalResult>(PluginProtocolJson.Options)!;
		Assert.Multiple(() =>
		{
			Assert.That(opened.Opened, Is.True);
			Assert.That(opened.ModalId, Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_link_the_plugin_opens_reaches_the_pressing_connection_that_can_open_links()
	{
		ClientFeatures.Declare(Connection, [UiClientFeatures.OpenUrl]);
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginOpenUrl,
			new UiOriginOpenUrlArguments
			{
				SessionId = sessionId, ClientId = ClientId, Url = "https://example.com/article"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Value.Deserialize<UiOriginOpenUrlResult>(PluginProtocolJson.Options)!.Delivered,
				Is.True);
			Assert.That(UiTransport.MessagesFor<UiOpenUrlEvent>(Connection).Single().Url,
				Is.EqualTo("https://example.com/article"));
		});
	}

	[Test]
	public async Task A_link_is_not_delivered_to_a_client_that_did_not_say_it_can_open_links()
	{
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginOpenUrl,
			new UiOriginOpenUrlArguments
			{
				SessionId = sessionId, ClientId = ClientId, Url = "https://example.com/article"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Value.Deserialize<UiOriginOpenUrlResult>(PluginProtocolJson.Options)!.Delivered,
				Is.False);
			Assert.That(UiTransport.MessagesFor<UiOpenUrlEvent>(Connection), Is.Empty);
		});
	}

	[Test]
	public async Task A_link_that_is_not_http_is_refused()
	{
		ClientFeatures.Declare(Connection, [UiClientFeatures.OpenUrl]);
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync(HostOperations.Ui.OriginOpenUrl,
			new UiOriginOpenUrlArguments { SessionId = sessionId, ClientId = ClientId, Url = "javascript:alert(1)" });

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(UiTransport.MessagesFor<UiOpenUrlEvent>(Connection), Is.Empty);
		});
	}

	[Test]
	public async Task An_origin_operation_this_host_does_not_know_answers_as_an_older_host_answers_open_url()
	{
		var sessionId = await OpenPressedWidgetAsync();

		var result = await InvokeAsync("origin.not-yet-invented",
			new UiOriginOpenUrlArguments
			{
				SessionId = sessionId, ClientId = ClientId, Url = "https://example.com/article"
			});

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}
}
