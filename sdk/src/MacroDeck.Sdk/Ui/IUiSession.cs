using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;

namespace MacroDeck.Sdk.Ui;

/// <summary>One provider-side UI session: the tree the host relays and the events it delivers back.</summary>
/// <remarks>
/// Deliberately expressed in <c>MacroDeck.Ui.Model</c> terms rather than the <c>MacroDeck.Ui</c> DSL, so
/// a provider can serve a tree without taking the DSL as a dependency. A DSL user forwards these members
/// to their <c>UiView</c>.
/// </remarks>
public interface IUiSession : IAsyncDisposable
{
	/// <summary>The session's current full tree. The host calls this when a client attaches and whenever
	/// it needs to resynchronise, so it must always describe the same revision the emitted patches have
	/// reached.</summary>
	UiTree BuildTree();

	/// <summary>Takes and clears the patches produced since the last call. The host relays them in order
	/// and never re-requests them, so a patch dropped here is lost to every attached client.</summary>
	IReadOnlyList<UiPatch> DrainPatches();

	/// <summary>Raised when patches are waiting. May be raised from any thread, and coalescing several
	/// changes into one raise is expected - the host drains rather than counting raises.</summary>
	event EventHandler? Changed;

	/// <summary>Raised when this session can no longer be served. The host turns it into a terminal,
	/// client-visible session error; a provider that can recover should not raise it.</summary>
	event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	/// <summary>Delivers a client event. Never called concurrently for one session. Rejecting an event is
	/// expressed by producing no patch, not by throwing - a throw faults the session.</summary>
	void Dispatch(UiEvent uiEvent);
}

/// <summary>Why a session can no longer be served.</summary>
public sealed class UiSessionFaultedEventArgs : EventArgs
{
	public UiSessionFaultedEventArgs(string reason, Exception? exception = null)
	{
		Reason = reason;
		Exception = exception;
	}

	public string Reason { get; }

	public Exception? Exception { get; }
}
