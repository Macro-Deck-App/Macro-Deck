using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using MacroDeckHost.Infrastructure.Usb.Native;

namespace MacroDeckHost.Tests.UnitTests.Usb;

internal static class Eventually
{
	private static readonly TimeSpan _limit = TimeSpan.FromSeconds(10);

	public static async Task True(Func<bool> condition, string because)
	{
		var deadline = DateTime.UtcNow + _limit;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail($"Timed out waiting until {because}.");
			}

			await Task.Delay(5);
		}
	}
}

internal sealed class FakeLinkCarrier : ILinkCarrier
{
	private readonly Channel<byte[]> _toHost = Channel.CreateUnbounded<byte[]>();
	private readonly LinkFrameReader _hostFrames = new();
	private readonly List<LinkFrame> _written = [];
	private readonly Lock _gate = new();
	private byte[] _pending = [];

	public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? WriteOverride { get; set; }

	private int _writeAttempts;

	public int WriteAttempts => Volatile.Read(ref _writeAttempts);

	public bool Disposed { get; private set; }

	public IReadOnlyList<LinkFrame> Written
	{
		get
		{
			lock (_gate)
			{
				return _written.ToList();
			}
		}
	}

	public void Send(params LinkFrame[] frames) => SendRaw(frames.SelectMany(frame => frame.Encode()).ToArray());

	public void SendRaw(byte[] bytes) => _toHost.Writer.TryWrite(bytes);

	public void Lose() => _toHost.Writer.TryComplete(new LinkLostException("The test dropped the carrier."));

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (_pending.Length == 0)
		{
			try
			{
				_pending = await _toHost.Reader.ReadAsync(cancellationToken);
			}
			catch (ChannelClosedException ex) when (ex.InnerException is LinkLostException lost)
			{
				throw lost;
			}
		}

		var count = Math.Min(buffer.Length, _pending.Length);
		_pending.AsMemory(0, count).CopyTo(buffer);
		_pending = _pending[count..];
		return count;
	}

	public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _writeAttempts);
		if (WriteOverride is { } write)
		{
			return write(frame, cancellationToken);
		}

		lock (_gate)
		{
			var parsed = new List<LinkInbound>();
			_hostFrames.Append(frame.Span, DateTimeOffset.UnixEpoch, parsed);
			_written.AddRange(parsed.Where(item => !item.Resync).Select(item => item.Frame));
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	public async Task<LinkFrame> NextAsync(Func<LinkFrame, bool> match, int after = 0)
	{
		LinkFrame? found = null;
		await Eventually.True(() =>
		{
			found = Written.Skip(after).Where(match).Cast<LinkFrame?>().FirstOrDefault();
			return found is not null;
		}, "the host wrote the expected frame");
		return found!.Value;
	}

	public byte[] DataOn(ushort stream)
		=> Written.Where(frame => frame.Type == LinkFrameType.Data && frame.Stream == stream)
			.SelectMany(frame => frame.Payload.ToArray())
			.ToArray();
}

internal sealed class LoopbackTestDialer : ILinkStreamDialer, IDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly Channel<NetworkStream> _accepted = Channel.CreateUnbounded<NetworkStream>();
	private readonly List<TcpClient> _clients = [];
	private readonly SemaphoreSlim _oneDialAtATime = new(1, 1);

	public LoopbackTestDialer()
	{
		_listener.Start();
	}

	public bool Refuse { get; set; }

	public bool Fail { get; set; }

	public TaskCompletionSource? Hold { get; set; }

	private int _dials;

	public int Dials => Volatile.Read(ref _dials);

	public async ValueTask<Stream?> DialAsync(string deviceKey, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _dials);
		if (Refuse)
		{
			return null;
		}

		if (Fail)
		{
			throw new SocketException((int)SocketError.ConnectionRefused);
		}

		if (Hold is { } hold)
		{
			await hold.Task.WaitAsync(cancellationToken);
		}

		await _oneDialAtATime.WaitAsync(cancellationToken);
		try
		{
			var client = new TcpClient { NoDelay = true };
			await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port, cancellationToken);
			var server = await _listener.AcceptTcpClientAsync(cancellationToken);
			lock (_clients)
			{
				_clients.Add(client);
				_clients.Add(server);
			}

			_accepted.Writer.TryWrite(server.GetStream());
			return client.GetStream();
		}
		finally
		{
			_oneDialAtATime.Release();
		}
	}

	public async Task<NetworkStream> AcceptedAsync()
		=> await _accepted.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

	public void Dispose()
	{
		_listener.Stop();
		_oneDialAtATime.Dispose();
		lock (_clients)
		{
			foreach (var client in _clients)
			{
				client.Dispose();
			}
		}
	}
}

internal static class StreamAssertions
{
	public static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
	{
		var buffer = new byte[count];
		await stream.ReadExactlyAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
		return buffer;
	}

	public static async Task<bool> ClosesAsync(Stream stream)
	{
		try
		{
			var buffer = new byte[1];
			return await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10)) == 0;
		}
		catch (IOException)
		{
			return true;
		}
	}
}
