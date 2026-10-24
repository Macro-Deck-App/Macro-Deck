using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class UiPageAndUploadContractTests : UiContractFixture
{
	private const string PageTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"page\",\"sessionMode\":\"exclusive\",\"attributes\":{}},"
		+ "\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	private TestUiCapabilityHandler _handler = null!;

	private async Task ConnectPagePluginAsync()
	{
		_handler = new TestUiCapabilityHandler
		{
			SurfaceKind = UiSurfaceKinds.Page,
			Pages = [new UiPageDescriptorDto { Id = "scripts", Title = "Scripts", IconName = "file-code" }],
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, PageTree))
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
	}

	private async Task<string> OpenPageAsync()
	{
		var opener = new PageUiSessionOpener(IntegrationRegistry, SnapshotStore, Broker, new ConnectedPlugin());
		var ticket = await opener.Open(new OpenPageUiSessionRequest { IntegrationId = PluginId, PageId = "scripts" },
				OwnerPrincipal,
				true)
			.Ready;

		Assert.That(ticket.Accepted, Is.True, $"The plugin's page did not open: {ticket.Code} {ticket.Message}");
		return ticket.SessionId;
	}

	private Task<HostCallbackResult> ReadUploadAsync(string sessionId, string uploadId)
	{
		return HostInvokeHandler!(Guid.NewGuid().ToString("N"),
			new HostInvokePayload
			{
				Api = HostApis.Ui,
				Operation = HostOperations.Ui.OriginReadUpload,
				Arguments = JsonSerializer.SerializeToElement(
					new UiOriginReadUploadArguments { SessionId = sessionId, UploadId = uploadId },
					PluginProtocolJson.Options)
			},
			CancellationToken.None);
	}

	[Test]
	public async Task A_declared_page_reaches_the_plugin_as_a_page_session_naming_the_page()
	{
		await ConnectPagePluginAsync();

		await OpenPageAsync();
		await WaitForUiAsync(() => _handler.Opened.Count == 1, "The page session never reached the plugin.");

		var opened = _handler.Opened.Single();
		Assert.Multiple(() =>
		{
			Assert.That(SnapshotStore.GetSnapshot(PluginId).UiPages.Single().Id, Is.EqualTo("scripts"));
			Assert.That(opened.SurfaceKind, Is.EqualTo("page"));
			Assert.That(opened.SurfaceAttributes!.Value.GetProperty("pageId").GetString(), Is.EqualTo("scripts"));
			Assert.That(opened.SurfaceAttributes!.Value.GetProperty("integrationId").GetString(),
				Is.EqualTo(PluginId));
		});
	}

	[Test]
	public async Task A_plugin_reads_an_upload_of_its_own_session_and_the_bytes_follow_as_a_ui_upload()
	{
		await ConnectPagePluginAsync();
		var sessionId = await OpenPageAsync();
		var text = "MsgBox \"hi\""u8.ToArray();
		var upload = UiUploads.Add(sessionId, "editor", OwnerPrincipal, text).Upload!;

		var result = await ReadUploadAsync(sessionId, upload.UploadId);

		var answer = result.Data!.Value.Deserialize<UiOriginReadUploadResult>(PluginProtocolJson.Options)!;
		var transfer = await PluginHostAssets.AwaitAsync(answer.TransferId!, CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Multiple(() =>
		{
			Assert.That(answer.Found, Is.True);
			Assert.That(answer.ByteLength, Is.EqualTo(text.Length));
			Assert.That(answer.ContentHash, Is.EqualTo(AssetContentHash.Compute(text)));
			Assert.That(transfer.Content, Is.EqualTo(text));
		});
	}

	[Test]
	public async Task Another_plugins_upload_is_not_found()
	{
		await ConnectPagePluginAsync();
		var foreignSessionId = Guid.NewGuid().ToString("N");
		UiRegistry.Create(foreignSessionId,
			"com.example.other",
			new UiSurface { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive },
			OwnerPrincipal);
		var upload = UiUploads.Add(foreignSessionId, "editor", OwnerPrincipal, "secret"u8.ToArray()).Upload!;

		var result = await ReadUploadAsync(foreignSessionId, upload.UploadId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Value.Deserialize<UiOriginReadUploadResult>(PluginProtocolJson.Options)!.Found,
				Is.False);
			Assert.That(result.Data!.Value.Deserialize<UiOriginReadUploadResult>(PluginProtocolJson.Options)!
					.TransferId,
				Is.Null);
		});
	}

	private sealed class ConnectedPlugin : IRemotePluginConnectionState
	{
		public bool IsConnected(string pluginId)
		{
			return string.Equals(pluginId, PluginId, StringComparison.Ordinal);
		}
	}
}
