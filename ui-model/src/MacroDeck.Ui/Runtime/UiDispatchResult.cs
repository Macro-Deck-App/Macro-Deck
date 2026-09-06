namespace MacroDeck.Ui.Runtime;

/// <summary>How a view answered a dispatched event.</summary>
public enum UiDispatchOutcome
{
	/// <summary>The event was handled. Whatever it changed is in the patch the dispatch produced.</summary>
	Accepted,

	/// <summary>
	/// Nothing here answers to this event, so nothing happened. A node id the tree does not have, or an event
	/// name the node does not accept: both are what a newer renderer talking to an older plugin looks like, and
	/// the model's posture is that an unknown name is non-fatal.
	/// </summary>
	Ignored,

	/// <summary>The event was meant for this node but could not be honoured - a payload of the wrong shape, a
	/// read-only binding, or a handler that faulted.</summary>
	Rejected,
}

/// <summary>
/// What <see cref="UiView.Dispatch" /> returned: the outcome and, for anything but acceptance, why. The
/// distinction is load-bearing. Ignored means the surface does not know what the client is talking about and
/// the session carries on; rejected means it did know and refused, which is something the client can report.
/// Collapsing the two into a boolean would make a renderer's newer event name indistinguishable from a real
/// error.
/// </summary>
public readonly record struct UiDispatchResult
{
	private UiDispatchResult(UiDispatchOutcome outcome, string? reason)
	{
		Outcome = outcome;
		Reason = reason;
	}

	/// <summary>How the view answered.</summary>
	public UiDispatchOutcome Outcome { get; }

	/// <summary>Why the event was not accepted. Never <c>null</c> unless <see cref="IsAccepted" /> is true.
	/// </summary>
	public string? Reason { get; }

	/// <summary>Whether the event was handled.</summary>
	public bool IsAccepted => Outcome == UiDispatchOutcome.Accepted;

	/// <summary>The accepted result.</summary>
	public static UiDispatchResult Accepted() => new(UiDispatchOutcome.Accepted, null);

	/// <summary>An ignored result carrying <paramref name="reason" />.</summary>
	public static UiDispatchResult Ignored(string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(reason);

		return new UiDispatchResult(UiDispatchOutcome.Ignored, reason);
	}

	/// <summary>A rejected result carrying <paramref name="reason" />.</summary>
	public static UiDispatchResult Rejected(string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(reason);

		return new UiDispatchResult(UiDispatchOutcome.Rejected, reason);
	}
}

/// <summary>
/// The fault an event handler produced, reported on <see cref="UiView.HandlerFaulted" /> instead of thrown.
/// One handler getting something wrong must not take the dispatch loop - and with it the whole surface - down
/// with it, and an asynchronous handler's fault must not go unobserved either.
/// </summary>
public sealed class UiHandlerFaultEventArgs : EventArgs
{
	/// <summary>Creates the arguments for a fault raised by <paramref name="eventName" /> on
	/// <paramref name="nodeId" />.</summary>
	public UiHandlerFaultEventArgs(string nodeId, string eventName, Exception exception)
	{
		NodeId = nodeId;
		EventName = eventName;
		Exception = exception;
	}

	/// <summary>The node whose handler faulted.</summary>
	public string NodeId { get; }

	/// <summary>The event the handler answered to.</summary>
	public string EventName { get; }

	/// <summary>What the handler threw.</summary>
	public Exception Exception { get; }
}
