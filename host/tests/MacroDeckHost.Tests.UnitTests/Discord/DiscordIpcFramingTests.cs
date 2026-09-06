using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordIpcFramingTests
{
	[Test]
	public async Task A_written_frame_reads_back_unchanged()
	{
		using var stream = new MemoryStream();
		var payload = Encoding.UTF8.GetBytes("""{"cmd":"AUTHENTICATE"}""");

		await DiscordIpcFraming.WriteFrameAsync(stream, DiscordRpcOpcode.Frame, payload, CancellationToken.None);
		stream.Position = 0;

		var frame = await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None);

		Assert.That(frame, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(frame!.Value.Opcode, Is.EqualTo(DiscordRpcOpcode.Frame));
			Assert.That(frame.Value.Payload, Is.EqualTo(payload));
		});
	}

	[Test]
	public async Task Frames_are_read_in_order()
	{
		using var stream = new MemoryStream();
		await DiscordIpcFraming.WriteFrameAsync(stream,
			DiscordRpcOpcode.Handshake,
			Encoding.UTF8.GetBytes("first"),
			CancellationToken.None);
		await DiscordIpcFraming.WriteFrameAsync(stream,
			DiscordRpcOpcode.Frame,
			Encoding.UTF8.GetBytes("second"),
			CancellationToken.None);
		stream.Position = 0;

		var first = await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None);
		var second = await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first!.Value.Opcode, Is.EqualTo(DiscordRpcOpcode.Handshake));
			Assert.That(Encoding.UTF8.GetString(first.Value.Payload), Is.EqualTo("first"));
			Assert.That(second!.Value.Opcode, Is.EqualTo(DiscordRpcOpcode.Frame));
			Assert.That(Encoding.UTF8.GetString(second.Value.Payload), Is.EqualTo("second"));
		});
	}

	[Test]
	public async Task An_empty_stream_reads_as_a_clean_close()
	{
		using var stream = new MemoryStream();

		var frame = await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None);

		Assert.That(frame, Is.Null);
	}

	[Test]
	public void A_truncated_header_is_a_protocol_error_not_a_clean_close()
	{
		using var stream = new MemoryStream([1, 0, 0]);

		Assert.ThrowsAsync<DiscordIpcProtocolException>(async () =>
			await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None));
	}

	[Test]
	public void A_truncated_payload_is_a_protocol_error()
	{
		using var stream = new MemoryStream();
		stream.Write(Header(DiscordRpcOpcode.Frame, 16));
		stream.Write([1, 2, 3]);
		stream.Position = 0;

		Assert.ThrowsAsync<DiscordIpcProtocolException>(async () =>
			await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None));
	}

	[Test]
	public void An_oversized_length_is_rejected_before_allocating()
	{
		using var stream = new MemoryStream(Header(DiscordRpcOpcode.Frame, DiscordIpcFraming.MaxPayloadLength + 1));

		Assert.ThrowsAsync<DiscordIpcProtocolException>(async () =>
			await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None));
	}

	[Test]
	public void A_negative_length_is_rejected()
	{
		using var stream = new MemoryStream(Header(DiscordRpcOpcode.Frame, -1));

		Assert.ThrowsAsync<DiscordIpcProtocolException>(async () =>
			await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None));
	}

	[Test]
	public void An_unknown_opcode_is_rejected()
	{
		using var stream = new MemoryStream(Header((DiscordRpcOpcode)99, 0));

		Assert.ThrowsAsync<DiscordIpcProtocolException>(async () =>
			await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None));
	}

	[Test]
	public async Task An_empty_payload_is_a_valid_frame()
	{
		using var stream = new MemoryStream();
		await DiscordIpcFraming.WriteFrameAsync(stream,
			DiscordRpcOpcode.Ping,
			ReadOnlyMemory<byte>.Empty,
			CancellationToken.None);
		stream.Position = 0;

		var frame = await DiscordIpcFraming.ReadFrameAsync(stream, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(frame!.Value.Opcode, Is.EqualTo(DiscordRpcOpcode.Ping));
			Assert.That(frame.Value.Payload, Is.Empty);
		});
	}

	[Test]
	public async Task The_header_is_written_little_endian()
	{
		using var stream = new MemoryStream();

		await DiscordIpcFraming.WriteFrameAsync(stream,
			DiscordRpcOpcode.Close,
			Encoding.UTF8.GetBytes("ab"),
			CancellationToken.None);

		byte[] expected = [2, 0, 0, 0, 2, 0, 0, 0];
		Assert.That(stream.ToArray()[..8], Is.EqualTo(expected));
	}

	private static byte[] Header(DiscordRpcOpcode opcode, int length)
	{
		var header = new byte[DiscordIpcFraming.HeaderLength];
		BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), (int)opcode);
		BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), length);
		return header;
	}
}
