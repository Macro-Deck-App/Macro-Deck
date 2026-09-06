using System.Text.Json;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="PluginAssetUploader" /> against the SDK's own fakes, mirroring <see cref="HostInvokerTests" />'s
/// shape for the same connect-and-drive-a-fake-socket pattern applied to the <c>asset.*</c> exchange.
/// </summary>
[TestFixture]
public class PluginAssetUploaderTests
{
	private static (PluginAssetUploader Uploader, FakePluginSocket Socket, Task<ConnectionOutcome> Run) Connect(
		TimeProvider timeProvider)
	{
		var state = new PluginConnectionState();
		var uploader = new PluginAssetUploader(state, timeProvider, Serilog.Core.Logger.None);

		var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker: null,
			hostStateCache: null,
			assetUploader: uploader);

		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		return (uploader, socket, run);
	}

	private static async Task AwaitHandshakeAsync(FakePluginSocket socket) =>
		await socket.NextAsync(MessageTypes.SessionHello);

	private static void Accept(FakePluginSocket socket, ProtocolEnvelope step)
	{
		var payload = step.Payload!.Value.Deserialize<PayloadWithAssetId>(PluginProtocolJson.Options)!;
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.AssetAck,
			Id = Guid.NewGuid().ToString(),
			CorrelationId = step.Id,
			Payload = FakePluginSocket.Payload(new AssetAckPayload { AssetId = payload.AssetId, Accepted = true })
		});
	}

	private static void Reject(FakePluginSocket socket, ProtocolEnvelope step)
	{
		var payload = step.Payload!.Value.Deserialize<PayloadWithAssetId>(PluginProtocolJson.Options)!;
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.AssetAck,
			Id = Guid.NewGuid().ToString(),
			CorrelationId = step.Id,
			Payload = FakePluginSocket.Payload(new AssetAckPayload { AssetId = payload.AssetId, Accepted = false })
		});
	}

	[Test]
	public async Task A_small_asset_travels_as_begin_then_one_chunk_then_commit_and_returns_the_content_hash()
	{
		var (uploader, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var data = new byte[] { 1, 2, 3, 4, 5 };
		var uploadTask = uploader.UploadAsync(AssetKinds.Icon, "image/png", data, CancellationToken.None);

		var begin = await socket.NextAsync(MessageTypes.AssetBegin);
		var beginPayload = begin.Payload!.Value.Deserialize<AssetBeginPayload>(PluginProtocolJson.Options)!;
		Assert.Multiple(() =>
		{
			Assert.That(beginPayload.Kind, Is.EqualTo(AssetKinds.Icon));
			Assert.That(beginPayload.MimeType, Is.EqualTo("image/png"));
			Assert.That(beginPayload.TotalBytes, Is.EqualTo(data.Length));
			Assert.That(beginPayload.ContentHash, Is.EqualTo(AssetContentHash.Compute(data)));
		});
		Accept(socket, begin);

		var chunk = await socket.NextAsync(MessageTypes.AssetChunk);
		var chunkPayload = chunk.Payload!.Value.Deserialize<AssetChunkPayload>(PluginProtocolJson.Options)!;
		Assert.Multiple(() =>
		{
			Assert.That(chunkPayload.Index, Is.EqualTo(0));
			Assert.That(Convert.FromBase64String(chunkPayload.Data), Is.EqualTo(data));
		});
		Accept(socket, chunk);

		var commit = await socket.NextAsync(MessageTypes.AssetCommit);
		Accept(socket, commit);

		var contentHash = await uploadTask;
		Assert.That(contentHash, Is.EqualTo(AssetContentHash.Compute(data)));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task An_asset_larger_than_one_chunk_is_split_into_multiple_ordered_chunks()
	{
		var (uploader, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var data = new byte[(int)(ProtocolLimits.MaxAssetChunkBytes * 2.5)];
		Random.Shared.NextBytes(data);
		var uploadTask = uploader.UploadAsync(AssetKinds.Artwork, "image/jpeg", data, CancellationToken.None);

		var begin = await socket.NextAsync(MessageTypes.AssetBegin);
		Accept(socket, begin);

		var reassembled = new List<byte>();
		for (var expectedIndex = 0; reassembled.Count < data.Length; expectedIndex++)
		{
			var chunk = await socket.NextAsync(MessageTypes.AssetChunk);
			var chunkPayload = chunk.Payload!.Value.Deserialize<AssetChunkPayload>(PluginProtocolJson.Options)!;

			Assert.That(chunkPayload.Index, Is.EqualTo(expectedIndex));
			var chunkBytes = Convert.FromBase64String(chunkPayload.Data);
			Assert.That(chunkBytes.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxAssetChunkBytes));

			reassembled.AddRange(chunkBytes);
			Accept(socket, chunk);
		}

		var commit = await socket.NextAsync(MessageTypes.AssetCommit);
		Accept(socket, commit);

		await uploadTask;
		Assert.That(reassembled, Is.EqualTo(data));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task An_asset_over_the_asset_size_limit_fails_fast_without_sending_anything()
	{
		var (uploader, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var oversized = new byte[ProtocolLimits.MaxAssetBytes + 1];

		Assert.ThrowsAsync<AssetUploadException>(async ()
			=> await uploader.UploadAsync(AssetKinds.Icon, "image/png", oversized, CancellationToken.None));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task The_host_rejecting_a_step_fails_the_upload()
	{
		var (uploader, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var uploadTask = uploader.UploadAsync(AssetKinds.Icon, "image/png", [1, 2, 3], CancellationToken.None);

		var begin = await socket.NextAsync(MessageTypes.AssetBegin);
		Reject(socket, begin);

		Assert.ThrowsAsync<AssetUploadException>(async () => await uploadTask);

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task The_asset_upload_budget_expiring_fails_the_whole_upload_not_just_one_step()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		var (uploader, socket, run) = Connect(time);
		await AwaitHandshakeAsync(socket);

		var uploadTask = uploader.UploadAsync(AssetKinds.Icon, "image/png", [1, 2, 3], CancellationToken.None);

		// Accepted, but the host never gets around to the chunk - the budget covers the whole upload,
		// not just the step it happens to be waiting on when it elapses.
		var begin = await socket.NextAsync(MessageTypes.AssetBegin);
		Accept(socket, begin);
		await socket.NextAsync(MessageTypes.AssetChunk);

		time.Advance(ProtocolTimeouts.AssetUpload);

		Assert.ThrowsAsync<AssetUploadException>(async () => await uploadTask);

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task Caller_cancellation_fails_the_upload_promptly()
	{
		var (uploader, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		using var cts = new CancellationTokenSource();
		var uploadTask = uploader.UploadAsync(AssetKinds.Icon, "image/png", [1, 2, 3], cts.Token);

		await socket.NextAsync(MessageTypes.AssetBegin);
		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await uploadTask);

		socket.CloseFromHost(1000);
		await run;
	}

	private sealed record PayloadWithAssetId
	{
		public string AssetId { get; init; } = string.Empty;
	}
}
