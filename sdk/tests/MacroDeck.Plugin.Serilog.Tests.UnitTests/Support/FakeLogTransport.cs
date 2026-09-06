using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>
/// A transport a test fully controls: whether it looks connected, what happens on send, and what it
/// actually received - so <c>MacroDeckLogShipper</c> can be driven without a real socket or host.
/// </summary>
internal sealed class FakeLogTransport : IMacroDeckLogTransport
{
	private readonly List<ProtocolEnvelope> _sent = [];
	private readonly Lock _gate = new();

	public bool IsConnected { get; set; } = true;

	/// <summary>Runs on every send attempt, before the envelope is recorded as delivered - throwing
	/// here simulates a transport failure.</summary>
	public Func<ProtocolEnvelope, Task>? OnSend { get; set; }

	public int AttemptCount { get; private set; }

	public IReadOnlyList<ProtocolEnvelope> Sent
	{
		get
		{
			lock (_gate)
			{
				return [.. _sent];
			}
		}
	}

	public async Task SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			AttemptCount++;
		}

		if (OnSend is { } onSend)
		{
			await onSend(envelope);
		}

		lock (_gate)
		{
			_sent.Add(envelope);
		}
	}
}
