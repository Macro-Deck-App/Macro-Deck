using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>
/// An in-memory socket. Standing in for the real one is what makes keepalive, backpressure and
/// close-code behaviour assertable without a port, a timing assumption or a sleep.
/// </summary>
internal sealed class FakePluginSocket : IPluginSocket
{
	private readonly Channel<byte[]> _fromHost = Channel.CreateUnbounded<byte[]>();
	private readonly Channel<ProtocolEnvelope> _toHost = Channel.CreateUnbounded<ProtocolEnvelope>();

	public int? CloseCode { get; private set; }

	/// <summary>Whether the connection was dropped without a close handshake.</summary>
	public bool Aborted { get; private set; }

	public string? CloseDescription { get; private set; }

	/// <summary>Everything the plugin has sent.</summary>
	public ChannelReader<ProtocolEnvelope> Sent => _toHost.Reader;

	/// <summary>When set, <see cref="SendAsync"/> parks on this instead of completing - simulating a
	/// host that stops draining its socket (issue #413 finding 1). Honours the caller's
	/// <see cref="CancellationToken"/> like the real send would.</summary>
	public TaskCompletionSource<bool>? SendGate { get; set; }

	public async Task SendAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
	{
		if (SendGate is { } gate)
		{
			await gate.Task.WaitAsync(cancellationToken);
		}

		var read = ProtocolEnvelopeReader.Read(utf8.Span);

		if (read.Envelope is { } envelope)
		{
			_toHost.Writer.TryWrite(envelope);
		}
	}

	public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _fromHost.Reader.ReadAsync(cancellationToken);
		}
		catch (ChannelClosedException)
		{
			return null;
		}
	}

	public Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
	{
		CloseCode ??= (int)status;
		CloseDescription = description;
		_fromHost.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public Task AbortAsync()
	{
		Aborted = true;
		_fromHost.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		_fromHost.Writer.TryComplete();
		_toHost.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}

	/// <summary>Queues a message from the host.</summary>
	public void Push(ProtocolEnvelope envelope)
		=> _fromHost.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope));

	/// <summary>Queues a raw body, for the malformed and unknown-type cases.</summary>
	public void PushRaw(string body) => _fromHost.Writer.TryWrite(Encoding.UTF8.GetBytes(body));

	/// <summary>Closes from the host's side with the given code, ending the plugin's receive loop.</summary>
	public void CloseFromHost(int closeCode, string? description = null)
	{
		CloseCode = closeCode;
		CloseDescription = description;
		_fromHost.Writer.TryComplete();
	}

	/// <summary>
	/// Waits for the next message of a given type, failing the test rather than hanging. Messages of
	/// other types are discarded, so use <see cref="NextAsync(TimeSpan?)" /> where the order matters.
	/// </summary>
	public async Task<ProtocolEnvelope> NextAsync(string type, TimeSpan? timeout = null)
	{
		using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));

		try
		{
			await foreach (var envelope in _toHost.Reader.ReadAllAsync(deadline.Token))
			{
				if (string.Equals(envelope.Type, type, StringComparison.Ordinal))
				{
					return envelope;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Swallowed for the message below: a cancelled channel read in a stack trace says nothing
			// about which message the test gave up waiting for.
		}

		throw new InvalidOperationException($"The plugin never sent a '{type}'.");
	}

	/// <summary>Waits for the next message whatever its type, for tests that assert the order.</summary>
	public async Task<ProtocolEnvelope> NextAsync(TimeSpan? timeout = null)
	{
		using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));

		try
		{
			return await _toHost.Reader.ReadAsync(deadline.Token);
		}
		catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException)
		{
			throw new InvalidOperationException("The plugin never sent another message.");
		}
	}

	public static JsonElement Payload<T>(T value) =>
		JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
}
