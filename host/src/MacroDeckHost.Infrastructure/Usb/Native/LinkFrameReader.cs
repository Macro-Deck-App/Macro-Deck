using System.Buffers.Binary;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal readonly record struct LinkInbound(bool Resync, LinkFrame Frame)
{
	public static LinkInbound ResyncMarker => new(true, default);
}

internal sealed class LinkFrameReader
{
	private const int HelloSignatureLength = LinkProtocol.HeaderLength + 4;

	private byte[] _buffer = new byte[LinkProtocol.MaxFrameLength * 2];
	private int _start;
	private int _end;
	private DateTimeOffset? _partialSince;

	public bool Seeking { get; private set; } = true;

	public void EnterSeeking()
	{
		Seeking = true;
		_partialSince = null;
	}

	public void Append(ReadOnlySpan<byte> data, DateTimeOffset now, List<LinkInbound> output)
	{
		EnsureCapacity(data.Length);
		data.CopyTo(_buffer.AsSpan(_end));
		_end += data.Length;
		Process(now, output);
	}

	public bool IsStuck(DateTimeOffset now)
		=> !Seeking && _partialSince is { } since && now - since >= LinkProtocol.IncompleteFrameTimeout;

	// The stuck frame's first byte is dropped and whatever arrived after it is rescanned for a HELLO,
	// so HELLOs resent while the frame hung are still found.
	public void Resync(DateTimeOffset now, List<LinkInbound> output)
	{
		output.Add(LinkInbound.ResyncMarker);
		if (_end > _start)
		{
			_start++;
		}

		EnterSeeking();
		Process(now, output);
	}

	private void Process(DateTimeOffset now, List<LinkInbound> output)
	{
		var frameBoundaryMoved = false;
		while (true)
		{
			if (Seeking)
			{
				if (!SeekHello())
				{
					break;
				}

				Seeking = false;
				frameBoundaryMoved = true;
				continue;
			}

			var available = _end - _start;
			if (available == 0)
			{
				break;
			}

			var span = _buffer.AsSpan(_start, available);
			if (span[0] == LinkProtocol.Filler)
			{
				_start++;
				frameBoundaryMoved = true;
				continue;
			}

			if (!LinkProtocol.IsKnownType(span[0]))
			{
				Malformed(output);
				frameBoundaryMoved = true;
				continue;
			}

			if (available < LinkProtocol.HeaderLength)
			{
				break;
			}

			var type = (LinkFrameType)span[0];
			var stream = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
			var length = BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
			if (!HeaderIsValid(type, stream, length))
			{
				Malformed(output);
				frameBoundaryMoved = true;
				continue;
			}

			var frameLength = LinkProtocol.HeaderLength + (int)length;
			if (available < frameLength)
			{
				break;
			}

			var payload = span.Slice(LinkProtocol.HeaderLength, (int)length);
			if (type == LinkFrameType.Hello && !payload.StartsWith(LinkProtocol.HelloMagic))
			{
				Malformed(output);
				frameBoundaryMoved = true;
				continue;
			}

			output.Add(new LinkInbound(false, new LinkFrame(type, span[1], stream, payload.ToArray())));
			_start += frameLength;
			frameBoundaryMoved = true;
		}

		if (Seeking || _end == _start)
		{
			_partialSince = null;
		}
		else if (frameBoundaryMoved || _partialSince is null)
		{
			_partialSince = now;
		}

		Compact();
	}

	private static bool HeaderIsValid(LinkFrameType type, ushort stream, uint length)
		=> length <= LinkProtocol.MaxPayloadLength &&
			type switch
			{
				LinkFrameType.Hello => stream == LinkProtocol.ControlStream &&
					length >= LinkProtocol.HelloPayloadLength,
				LinkFrameType.Window => length == LinkProtocol.WindowPayloadLength,
				_ => true
			};

	private void Malformed(List<LinkInbound> output)
	{
		output.Add(LinkInbound.ResyncMarker);
		_start++;
		EnterSeeking();
	}

	private bool SeekHello()
	{
		var span = _buffer.AsSpan(_start, _end - _start);
		for (var offset = 0; offset < span.Length; offset++)
		{
			var matched = MatchesHelloPrefix(span[offset..]);
			if (matched == HelloSignatureLength)
			{
				_start += offset;
				return true;
			}

			if (matched == span.Length - offset)
			{
				_start += offset;
				return false;
			}
		}

		_start = _end;
		return false;
	}

	// Later versions may append to the HELLO payload, so the length is a range.
	private static int MatchesHelloPrefix(ReadOnlySpan<byte> candidate)
	{
		var length = Math.Min(candidate.Length, HelloSignatureLength);
		for (var index = 0; index < length; index++)
		{
			var matches = index switch
			{
				0 or 2 or 3 or 4 or 5 => candidate[index] == 0,
				1 or 7 => true,
				6 => candidate[index] <= LinkProtocol.MaxPayloadLength >> 8,
				_ => candidate[index] == LinkProtocol.HelloMagic[index - LinkProtocol.HeaderLength]
			};
			if (!matches)
			{
				return -1;
			}
		}

		if (length >= LinkProtocol.HeaderLength)
		{
			var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(candidate[4..]);
			if (payloadLength is < LinkProtocol.HelloPayloadLength or > LinkProtocol.MaxPayloadLength)
			{
				return -1;
			}
		}

		return length;
	}

	private void EnsureCapacity(int incoming)
	{
		if (_end + incoming <= _buffer.Length)
		{
			return;
		}

		Compact();
		if (_end + incoming <= _buffer.Length)
		{
			return;
		}

		Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _end + incoming));
	}

	private void Compact()
	{
		if (_start == 0)
		{
			return;
		}

		var remaining = _end - _start;
		if (remaining > 0)
		{
			Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
		}

		_start = 0;
		_end = remaining;
	}
}
