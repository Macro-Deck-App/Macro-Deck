using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Testing;

/// <summary>Which side of <see cref="MacroDeckTestHost" /> sent a <see cref="RecordedProtocolMessage" />.</summary>
public enum ProtocolMessageDirection
{
	/// <summary>Sent by the test host, playing the Macro Deck host's part.</summary>
	ToPlugin,

	/// <summary>Sent by the plugin under test.</summary>
	FromPlugin
}

/// <summary>One envelope observed on the wire, with the direction it travelled.</summary>
public sealed record RecordedProtocolMessage
{
	/// <summary>Which side sent it.</summary>
	public required ProtocolMessageDirection Direction { get; init; }

	/// <summary>The envelope exactly as it was sent or received - never re-serialized for this record.</summary>
	public required ProtocolEnvelope Envelope { get; init; }

	/// <summary>When the test host observed it.</summary>
	public required DateTimeOffset RecordedAt { get; init; }

	/// <summary>
	/// This log's monotonically increasing record order - lower means recorded first. Comparable
	/// directly against <see cref="RecordedProtocolClose.Sequence" /> to prove one happened before the
	/// other; wall-clock timestamps alone are not a reliable ordering signal at the resolution two
	/// back-to-back sends on the same connection can land within.
	/// </summary>
	public required long Sequence { get; init; }
}

/// <summary>One WebSocket close frame the test host sent from its own side of a connection.</summary>
public sealed record RecordedProtocolClose
{
	/// <summary>The close code the test host sent - one of <see cref="MacroDeck.Plugin.Protocol.Errors.ProtocolCloseCodes" />'s
	/// values, or a standard WebSocket code such as normal closure.</summary>
	public required int CloseCode { get; init; }

	/// <summary>When the test host sent it.</summary>
	public required DateTimeOffset RecordedAt { get; init; }

	/// <summary>See <see cref="RecordedProtocolMessage.Sequence" />.</summary>
	public required long Sequence { get; init; }
}

/// <summary>
/// Every envelope <see cref="MacroDeckTestHost" /> has sent or received, across every connection it has
/// accepted, in the order it saw them. A single log rather than one per session: several scenarios -
/// session replacement, a second launch - need to see traffic that spans more than one connection.
/// </summary>
public sealed class ProtocolMessageLog
{
	private readonly ConcurrentQueue<RecordedProtocolMessage> _messages = new();
	private readonly ConcurrentQueue<RecordedProtocolClose> _closes = new();
	private long _sequence;

	/// <summary>Every message recorded so far, in arrival order.</summary>
	public IReadOnlyList<RecordedProtocolMessage> All => [.. _messages];

	/// <summary>
	/// Every close frame the test host has sent from its own side, across every connection it has
	/// accepted, in the order it sent them. <c>PluginConnection.CloseAsync</c> is the only source: a
	/// close the plugin itself initiates is not captured as an entry here, only as the receive loop
	/// ending.
	/// </summary>
	public IReadOnlyList<RecordedProtocolClose> Closes => [.. _closes];

	/// <summary>Messages of a given <see cref="MacroDeck.Plugin.Protocol.Envelope.MessageTypes" /> value, in arrival order.</summary>
	public IReadOnlyList<RecordedProtocolMessage> OfType(string type)
		=> [.. All.Where(message => string.Equals(message.Envelope.Type, type, StringComparison.Ordinal))];

	/// <summary>Messages whose <c>correlationId</c> equals <paramref name="correlationId" />, in arrival order.</summary>
	public IReadOnlyList<RecordedProtocolMessage> WithCorrelationId(string correlationId)
		=>
		[
			.. All.Where(message =>
				string.Equals(message.Envelope.CorrelationId, correlationId, StringComparison.Ordinal))
		];

	internal void Record(ProtocolMessageDirection direction, ProtocolEnvelope envelope)
		=> _messages.Enqueue(new RecordedProtocolMessage
		{
			Direction = direction,
			Envelope = envelope,
			RecordedAt = DateTimeOffset.UtcNow,
			Sequence = NextSequence()
		});

	internal void RecordClose(int closeCode)
		=> _closes.Enqueue(new RecordedProtocolClose
		{
			CloseCode = closeCode,
			RecordedAt = DateTimeOffset.UtcNow,
			Sequence = NextSequence()
		});

	private long NextSequence() => Interlocked.Increment(ref _sequence);
}
