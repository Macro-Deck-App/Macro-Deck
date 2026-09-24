using System.Buffers.Binary;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal enum LinkFrameType : byte
{
	Hello = 0,

	Open = 1,

	Data = 2,

	Close = 3,

	Window = 4,

	Bye = 5
}

// Link protocol v1, mirrored by hand in the Companion app. ADR 0095 holds the contract.
internal static class LinkProtocol
{
	public const byte Version = 1;

	public const int HeaderLength = 8;

	public const int MaxFrameLength = 16384;

	public const int MaxPayloadLength = MaxFrameLength - HeaderLength;

	public const byte Filler = 0xFF;

	public const byte AckFlag = 0x01;

	public const int HelloPayloadLength = 13;

	public const int WindowPayloadLength = 4;

	public const int InitialWindow = 256 * 1024;

	public const ushort ControlStream = 0;

	public const int UsbFillerPacketSize = 64;

	public static readonly TimeSpan HelloInterval = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(5);

	public static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(15);

	public static readonly TimeSpan IncompleteFrameTimeout = TimeSpan.FromSeconds(2);

	public static readonly TimeSpan FrameWriteTimeout = TimeSpan.FromSeconds(15);

	public static ReadOnlySpan<byte> HelloMagic => "MDLK"u8;

	public static bool IsKnownType(byte type) => type <= (byte)LinkFrameType.Bye;

	// A frame whose length is a multiple of the packet size would end the USB transfer on a full packet,
	// so one filler byte makes it end on a short packet instead of relying on a zero-length packet.
	public static byte[] ForUsbTransfer(ReadOnlySpan<byte> frame)
	{
		var needsFiller = frame.Length > 0 &&
			frame.Length < MaxFrameLength &&
			frame.Length % UsbFillerPacketSize == 0;
		var transfer = new byte[frame.Length + (needsFiller ? 1 : 0)];
		frame.CopyTo(transfer);
		if (needsFiller)
		{
			transfer[^1] = Filler;
		}

		return transfer;
	}
}

internal readonly record struct LinkFrame(LinkFrameType Type, byte Flags, ushort Stream, ReadOnlyMemory<byte> Payload)
{
	public byte[] Encode()
	{
		if (Payload.Length > LinkProtocol.MaxPayloadLength)
		{
			throw new ArgumentOutOfRangeException(nameof(Payload), Payload.Length, "Link frame payload too large.");
		}

		var frame = new byte[LinkProtocol.HeaderLength + Payload.Length];
		frame[0] = (byte)Type;
		frame[1] = Flags;
		BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), Stream);
		BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)Payload.Length);
		Payload.Span.CopyTo(frame.AsSpan(LinkProtocol.HeaderLength));
		return frame;
	}

	public static LinkFrame Hello(bool ack, uint epoch, uint echo, byte maxVersion = LinkProtocol.Version)
	{
		var payload = new byte[LinkProtocol.HelloPayloadLength];
		LinkProtocol.HelloMagic.CopyTo(payload);
		payload[4] = maxVersion;
		BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(5), epoch);
		BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(9), echo);
		return new LinkFrame(LinkFrameType.Hello, ack ? LinkProtocol.AckFlag : (byte)0, LinkProtocol.ControlStream,
			payload);
	}

	public static LinkFrame Open(ushort stream) => new(LinkFrameType.Open, 0, stream, ReadOnlyMemory<byte>.Empty);

	public static LinkFrame Data(ushort stream, ReadOnlyMemory<byte> payload)
		=> new(LinkFrameType.Data, 0, stream, payload);

	public static LinkFrame Close(ushort stream) => new(LinkFrameType.Close, 0, stream, ReadOnlyMemory<byte>.Empty);

	public static LinkFrame Window(ushort stream, uint credit)
	{
		var payload = new byte[LinkProtocol.WindowPayloadLength];
		BinaryPrimitives.WriteUInt32BigEndian(payload, credit);
		return new LinkFrame(LinkFrameType.Window, 0, stream, payload);
	}

	public static LinkFrame Bye() => new(LinkFrameType.Bye, 0, LinkProtocol.ControlStream, ReadOnlyMemory<byte>.Empty);

	public uint Credit => BinaryPrimitives.ReadUInt32BigEndian(Payload.Span);
}

internal readonly record struct LinkHello(bool Ack, byte MaxVersion, uint Epoch, uint Echo)
{
	public static LinkHello Parse(LinkFrame frame)
	{
		var payload = frame.Payload.Span;
		return new LinkHello((frame.Flags & LinkProtocol.AckFlag) != 0,
			payload[4],
			BinaryPrimitives.ReadUInt32BigEndian(payload[5..]),
			BinaryPrimitives.ReadUInt32BigEndian(payload[9..]));
	}
}
