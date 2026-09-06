using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The promise the action-modal contract makes on a plugin's behalf: an action names one of its own
/// dialogs, and that plugin's tree is what the triggering client ends up looking at. The first-party
/// Weather dialog proves the shape is expressible, not that a plugin can reach it - the host resolves a
/// modal's provider from the id its action ran under, which for a plugin is the plugin itself.
/// </summary>
[TestFixture]
internal sealed class ActionModalContractTests : UiContractFixture
{
	private const string ViewId = "device-picker";

	[Test]
	public async Task A_modal_a_plugins_action_opened_is_served_by_that_plugin_and_reaches_the_client()
	{
		var tree = new UiTree
		{
			Revision = 1,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
			Root = new UiNode { Id = "root", Type = "ui.stack" }
		};
		var handler = new TestUiCapabilityHandler
		{
			SurfaceKind = UiSurfaceKinds.Dialog,
			OnSnapshotRequested = sessionId =>
				Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, UiCanonicalJson.Serialize(tree)))
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

		// Exactly what the callback router does for a plugin's show-modal: the modal is registered under
		// the plugin's own id, and that id is what the opener later resolves a provider from.
		var modalId = Modals.Register(PluginId,
			"c1",
			new ModalDefinition
			{
				ViewId = ViewId,
				Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					["deviceId"] = JsonSerializer.SerializeToElement("dev-7")
				}
			});
		Assert.That(modalId, Is.Not.Null, "The modal was refused before any provider was consulted.");

		var opener = new ModalUiSessionOpener(Modals, Broker);
		var opened = opener.Open(new OpenModalUiSessionRequest { ModalId = modalId! }, OwnerPrincipal);
		Assert.That(opened.Accepted, Is.True, $"The modal's session was refused: {opened.Code} {opened.Message}");

		var attach = Attach(opened.SessionId, "c1");

		// At least one, not exactly one: opening the session and attaching to it each ask the provider
		// for a snapshot, and which of the two a client sees first is a race it is not entitled to an
		// answer about. The requirement is that the plugin's dialog arrives, not how many times.
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count > 0,
			"The plugin's dialog never reached the client that triggered it.");

		var asked = handler.Opened.Single();
		var attributes = asked.SurfaceAttributes!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(asked.SurfaceKind,
				Is.EqualTo(UiSurfaceKinds.Dialog),
				"The plugin was asked for something other than a dialog.");
			Assert.That(attributes.GetProperty(UiDialogSurfaceAttributes.ViewId).GetString(),
				Is.EqualTo(ViewId),
				"The plugin cannot tell which of its dialogs to build.");
			Assert.That(attributes.GetProperty(UiDialogSurfaceAttributes.ModalId).GetString(),
				Is.EqualTo(modalId));
			Assert.That(attributes.GetProperty(UiDialogSurfaceAttributes.Data)
					.GetProperty("deviceId")
					.GetString(),
				Is.EqualTo("dev-7"),
				"The data the action passed did not reach the plugin.");
			Assert.That(attach.Accepted, Is.True, $"The attach was refused: {attach.Code}");
			Assert.That(attach.SurfaceKind, Is.EqualTo(UiSurfaceKinds.Dialog));
			Assert.That(attach.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
				Has.All.Matches<UiSessionTreeUpdatedEvent>(delivered => delivered.Revision == tree.Revision),
				"The client was shown a tree the plugin did not publish.");
		});
	}
}
