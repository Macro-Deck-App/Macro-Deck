using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Backups.Crypto;

/// <summary>
/// Read-only counterpart of <see cref="BackupEncryptingStream"/>. One byte of look-ahead is what tells a
/// full segment apart from the final one, so a payload that ends without a final-flagged segment is
/// reported as truncated rather than silently accepted.
/// </summary>
public sealed class BackupDecryptingStream : Stream
{
	private readonly Stream _source;
	private readonly bool _leaveOpen;
	private readonly AesGcm _cipher;
	private readonly BackupPayloadHeader _header;
	private readonly byte[] _headerBytes;
	private readonly byte[] _block;
	private readonly byte[] _plaintext;

	private int _plaintextOffset;
	private int _plaintextCount;
	private int _carry = -1;
	private uint _segment;
	private bool _finished;

	public BackupDecryptingStream(Stream source,
		BackupPayloadHeader header,
		byte[] headerBytes,
		byte[] dataKey,
		bool leaveOpen = false)
	{
		_source = source;
		_leaveOpen = leaveOpen;
		_header = header;
		_headerBytes = headerBytes;
		_cipher = new AesGcm(dataKey, BackupPayloadHeader.TagBytes);
		_block = new byte[header.ChunkSize + BackupPayloadHeader.TagBytes];
		_plaintext = new byte[header.ChunkSize];
	}

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		if (buffer.IsEmpty)
		{
			return 0;
		}

		if (_plaintextOffset == _plaintextCount && !FillNextSegment())
		{
			return 0;
		}

		var take = Math.Min(buffer.Length, _plaintextCount - _plaintextOffset);
		_plaintext.AsSpan(_plaintextOffset, take).CopyTo(buffer);
		_plaintextOffset += take;

		return take;
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_cipher.Dispose();
			CryptographicOperations.ZeroMemory(_plaintext);

			if (!_leaveOpen)
			{
				_source.Dispose();
			}
		}

		base.Dispose(disposing);
	}

	private bool FillNextSegment()
	{
		if (_finished)
		{
			return false;
		}

		var read = 0;
		if (_carry >= 0)
		{
			_block[0] = (byte)_carry;
			_carry = -1;
			read = 1;
		}

		read += ReadAtLeast(_block.AsSpan(read));
		var lookAhead = _source.ReadByte();
		var isFinal = lookAhead < 0;

		if (!isFinal)
		{
			_carry = lookAhead;

			if (read != _block.Length)
			{
				throw new BackupCryptoException(BackupDecryptResult.Corrupt,
					"The backup payload contains an incomplete segment.");
			}
		}
		else if (read < BackupPayloadHeader.TagBytes)
		{
			throw new BackupCryptoException(BackupDecryptResult.Truncated, "The backup payload ends mid-segment.");
		}

		var length = read - BackupPayloadHeader.TagBytes;
		try
		{
			_cipher.Decrypt(_header.SegmentNonce(_segment, isFinal),
				_block.AsSpan(0, length),
				_block.AsSpan(length, BackupPayloadHeader.TagBytes),
				_plaintext.AsSpan(0, length),
				_headerBytes);
		}
		catch (CryptographicException)
		{
			// A tampered segment and a payload whose final segment was cut off are indistinguishable here:
			// dropping the last segment leaves a segment that was written without the final flag, so it
			// fails the same authentication check. Both are reported as corrupt rather than guessing.
			throw new BackupCryptoException(BackupDecryptResult.Corrupt,
				"The backup payload failed authentication.");
		}

		_segment++;
		_plaintextOffset = 0;
		_plaintextCount = length;
		_finished = isFinal;

		return length > 0 || !isFinal;
	}

	private int ReadAtLeast(Span<byte> destination)
	{
		var total = 0;
		while (total < destination.Length)
		{
			var read = _source.Read(destination[total..]);
			if (read == 0)
			{
				break;
			}

			total += read;
		}

		return total;
	}
}
