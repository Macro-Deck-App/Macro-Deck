using System.Buffers.Binary;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal static class DiscordIpcFraming
{
	internal const int HeaderLength = 8;
	internal const int MaxPayloadLength = 4 * 1024 * 1024;

	public static async Task WriteFrameAsync(
		Stream stream,
		DiscordRpcOpcode opcode,
		ReadOnlyMemory<byte> payload,
		CancellationToken cancellationToken)
	{
		var frame = new byte[HeaderLength + payload.Length];
		BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), (int)opcode);
		BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
		payload.Span.CopyTo(frame.AsSpan(HeaderLength));

		await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	public static async Task<DiscordIpcFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
	{
		var header = new byte[HeaderLength];
		if (!await ReadExactlyAsync(stream, header, allowEof: true, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}

		var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
		var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));

		if (length is < 0 or > MaxPayloadLength)
		{
			throw new DiscordIpcProtocolException($"Frame payload length {length} is out of range.");
		}

		if (!Enum.IsDefined(typeof(DiscordRpcOpcode), opcode))
		{
			throw new DiscordIpcProtocolException($"Unknown frame opcode {opcode}.");
		}

		var payload = new byte[length];
		if (length > 0 &&
			!await ReadExactlyAsync(stream, payload, allowEof: false, cancellationToken).ConfigureAwait(false))
		{
			throw new DiscordIpcProtocolException("Stream ended inside a frame payload.");
		}

		return new DiscordIpcFrame((DiscordRpcOpcode)opcode, payload);
	}

	private static async Task<bool> ReadExactlyAsync(
		Stream stream,
		byte[] buffer,
		bool allowEof,
		CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream
				.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
				.ConfigureAwait(false);
			if (read == 0)
			{
				if (offset == 0 && allowEof)
				{
					return false;
				}

				throw new DiscordIpcProtocolException("Stream ended inside a frame header.");
			}

			offset += read;
		}

		return true;
	}
}
