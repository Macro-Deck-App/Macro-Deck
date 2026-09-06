using System.Security.Cryptography;

namespace MacroDeckHost.Infrastructure.Backups.Crypto;

/// <summary>
/// Write-only AES-256-GCM STREAM encryptor. A full buffer is only emitted once more data actually
/// arrives, so the segment written at close is always the one carrying the final-segment flag.
/// </summary>
public sealed class BackupEncryptingStream : Stream
{
	private readonly Stream _destination;
	private readonly bool _leaveOpen;
	private readonly AesGcm _cipher;
	private readonly BackupPayloadHeader _header;
	private readonly byte[] _headerBytes;
	private readonly byte[] _buffer;
	private readonly byte[] _tag = new byte[BackupPayloadHeader.TagBytes];

	private int _pending;
	private uint _segment;
	private bool _finished;

	public BackupEncryptingStream(Stream destination,
		BackupPayloadHeader header,
		byte[] headerBytes,
		byte[] dataKey,
		bool leaveOpen = false)
	{
		_destination = destination;
		_leaveOpen = leaveOpen;
		_header = header;
		_headerBytes = headerBytes;
		_cipher = new AesGcm(dataKey, BackupPayloadHeader.TagBytes);
		_buffer = new byte[header.ChunkSize];
	}

	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		ObjectDisposedException.ThrowIf(_finished, this);

		while (!buffer.IsEmpty)
		{
			if (_pending == _buffer.Length)
			{
				EmitSegment(isFinal: false);
			}

			var take = Math.Min(_buffer.Length - _pending, buffer.Length);
			buffer[..take].CopyTo(_buffer.AsSpan(_pending));
			_pending += take;
			buffer = buffer[take..];
		}
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		Write(buffer.AsSpan(offset, count));

		return Task.CompletedTask;
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		Write(buffer.Span);

		return ValueTask.CompletedTask;
	}

	public void Finish()
	{
		if (_finished)
		{
			return;
		}

		EmitSegment(isFinal: true);
		_finished = true;
		_destination.Flush();
	}

	public override void Flush() => _destination.Flush();

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Finish();
			_cipher.Dispose();
			CryptographicOperations.ZeroMemory(_buffer);

			if (!_leaveOpen)
			{
				_destination.Dispose();
			}
		}

		base.Dispose(disposing);
	}

	private void EmitSegment(bool isFinal)
	{
		if (!isFinal && _segment == uint.MaxValue)
		{
			throw new InvalidOperationException("The backup payload exceeds the maximum segment count.");
		}

		var ciphertext = new byte[_pending];
		_cipher.Encrypt(_header.SegmentNonce(_segment, isFinal),
			_buffer.AsSpan(0, _pending),
			ciphertext,
			_tag,
			_headerBytes);

		_destination.Write(ciphertext);
		_destination.Write(_tag);

		_segment++;
		_pending = 0;
	}
}
