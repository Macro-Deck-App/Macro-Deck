namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Options for one <c>PluginSessionView.InvokeAsync</c> or <c>PluginTestHarness.InvokeAsync</c> call.
///
/// <para>
/// <see cref="Timeout" /> and <see cref="Deadline" /> both become the invocation's wire-level
/// <c>deadlineMs</c> - the same field <c>CapabilityInvokePayload</c> documents as living on the
/// envelope, never the payload. When both are set, <see cref="Deadline" /> wins. The deadline this
/// produces is <strong>wall-clock</strong>: the SDK-side dispatcher enforces it with <c>CancelAfter</c>
/// on a plain <see cref="CancellationTokenSource" />, so it is <em>not</em> controllable by
/// <see cref="ManualTimeProvider" /> - only keepalive and reconnect backoff honour an injected clock.
/// Use a real, short <see cref="Timeout" /> to exercise a timeout; do not try to produce one by
/// advancing a fake clock.
/// </para>
///
/// <para>
/// <see cref="CancellationToken" /> only abandons this call's local wait - it does not by itself tell
/// the plugin anything. To make the plugin observe a withdrawal, call
/// <c>PluginSessionView.CancelAsync</c> (or, on <see cref="PluginTestHarness" />, let this token expire,
/// which it links into the invocation directly).
/// </para>
/// </summary>
public sealed class CapabilityInvokeOptions
{
	/// <summary>How long the invocation gets before it is treated as timed out. Wall-clock; see the type remarks.</summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary>When the invocation is treated as timed out. Wall-clock; see the type remarks. Wins over <see cref="Timeout" /> when both are set.</summary>
	public DateTimeOffset? Deadline { get; init; }

	/// <summary>
	/// Marks the invocation as safely retryable under this key. Forwarded on the envelope exactly as
	/// given - this type does not implement replay itself; see <c>MacroDeckTestHost</c>'s remarks on why.
	/// </summary>
	public string? IdempotencyKey { get; init; }

	/// <summary>Cancels this call's local wait. See the type remarks for what it does and does not do.</summary>
	public CancellationToken CancellationToken { get; init; }
}
