using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Icons;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class IconsContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Icons,
			LocalId = IconsCapabilityHandler.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static byte[] RandomBytes(int length)
	{
		var bytes = new byte[length];
		Random.Shared.NextBytes(bytes);
		return bytes;
	}

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var bytes = RandomBytes(64);
		var integration = await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(integration, Is.InstanceOf<IIntegrationIconProvider>());
		});
	}

	[Test]
	public async Task A_multi_chunk_icon_arrives_and_GetIcon_serves_exactly_those_bytes()
	{
		var bytes = RandomBytes((int)(ProtocolLimits.MaxAssetChunkBytes * 2.5));
		var integration = await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/webp"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/webp", bytes));

		var iconProvider = (IIntegrationIconProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(iconProvider.GetIcon(), Is.EqualTo(bytes));
			Assert.That(iconProvider.IconMimeType, Is.EqualTo("image/webp"));
		});
	}

	[Test]
	public async Task Describe_reports_mime_type_byte_length_and_content_hash()
	{
		var bytes = RandomBytes(200);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/svg+xml"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/svg+xml", bytes));

		var described = await InvokeRawAsync(CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe);
		var payload = described!.Value.Deserialize<MacroDeck.Plugin.Protocol.Capabilities.Icons.IconsDescribePayload>(
			MacroDeck.Plugin.Protocol.Serialization.PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(payload!.MimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(payload.ByteLength, Is.EqualTo(bytes.Length));
			Assert.That(payload.ContentHash, Is.EqualTo(AssetContentHash.Compute(bytes)));
		});
	}

	[Test]
	public async Task The_icon_never_arriving_registers_the_adapter_without_the_interface_promptly()
	{
		var bytes = RandomBytes(64);

		var connectTask = ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons]);

		await Task.Delay(300);

		for (var attempt = 0; attempt < 200 && !connectTask.IsCompleted; attempt++)
		{
			await Task.Yield();
			Time.Advance(ProtocolTimeouts.AssetUpload);
		}

		var integration = await connectTask;

		Assert.That(integration, Is.Not.InstanceOf<IIntegrationIconProvider>());
	}

	[Test]
	public async Task A_fresh_commit_with_a_different_content_hash_replaces_the_cached_icon_mid_session()
	{
		var original = RandomBytes(64);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(original, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", original));

		var replacement = RandomBytes(64);
		await UploadAssetAsync(AssetKinds.Icon, "image/png", replacement);

		byte[]? servedBytes = null;
		for (var attempt = 0; attempt < 200; attempt++)
		{
			await Task.Delay(5);
			var current = IntegrationRegistry.Integrations.SingleOrDefault(i => i.Id == PluginId);
			servedBytes = (current as IIntegrationIconProvider)?.GetIcon();

			if (servedBytes is { Length: > 0 } && servedBytes.SequenceEqual(replacement))
			{
				break;
			}
		}

		Assert.That(servedBytes, Is.EqualTo(replacement));
	}

	[Test]
	public async Task Oversized_artwork_now_travels_by_asset_instead_of_failing()
	{
		await ConnectAsync([new IconsCapabilityHandler(IconAssetSource.None)], [], []);

		var artwork = RandomBytes(200 * 1024);
		var contentHash = await UploadAssetAsync(AssetKinds.Artwork, "image/jpeg", artwork);

		Assert.Multiple(() =>
		{
			Assert.That(contentHash, Is.EqualTo(AssetContentHash.Compute(artwork)));
			Assert.That(AssetCache.TryRead(contentHash, out var cached, out var mimeType), Is.True);
			Assert.That(cached, Is.EqualTo(artwork));
			Assert.That(mimeType, Is.EqualTo("image/jpeg"));
		});
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		// Not accepted, so RegisterAsync's own registration-time describe never touches this handler -
		// icons has no operation but describe, so a handler that throws unconditionally cannot ever
		// register successfully at all (by design - see the class remarks on "no fallback to retain").
		// InvokeRawAsync reaches it directly instead, the same way this kind's other direct-invoke tests do.
		await ConnectAsync([new ThrowingIconsCapabilityHandler()], [], []);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("boom"));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task Describe_ignores_the_local_id_and_an_unknown_operation_is_unsupported()
	{
		var bytes = RandomBytes(64);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		var arbitraryLocalId = await InvokeRawAsync(CapabilityKinds.Icons, "nope", CapabilityOperations.Icons.Describe);
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Icons, IconsCapabilityHandler.LocalId, "rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(arbitraryLocalId, Is.Not.Null);
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	[Test]
	public async Task Describing_an_icon_that_was_never_declared_reports_capability_unavailable()
	{
		await ConnectAsync([new IconsCapabilityHandler(IconAssetSource.None)], [], []);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe));

		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task A_dropped_reply_on_the_wire_times_out()
	{
		var bytes = RandomBytes(64);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		Link.DropNextReply();
		var invokeTask = InvokeRawAsync(CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await invokeTask);
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
	}

	[Test]
	public async Task A_dropped_connection_reports_capability_unavailable()
	{
		var bytes = RandomBytes(64);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		Disconnect();

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe));

		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var bytes = RandomBytes(64);
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[Provider()],
			[CapabilityKinds.Icons],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		using var cts = new CancellationTokenSource();
		using var hold = Link.HoldNextReply();
		var invokeTask = InvokeRawAsync(CapabilityKinds.Icons,
			IconsCapabilityHandler.LocalId,
			CapabilityOperations.Icons.Describe,
			cancellationToken: cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await invokeTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);
	}

	private sealed class ThrowingIconsCapabilityHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Icons;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
			=>
			[
				new()
				{
					Kind = CapabilityKinds.Icons, LocalId = IconsCapabilityHandler.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			];

		public Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("boom: token=abc123");
	}
}
