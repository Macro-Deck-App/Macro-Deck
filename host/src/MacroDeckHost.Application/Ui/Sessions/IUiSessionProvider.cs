using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Sessions;

// No member returns tree or patch data. Everything a provider produces travels the other way through
// IUiSessionSink, which is what lets a realtime operation answer a client without ever awaiting a
// provider.
public interface IUiSessionProvider
{
	// Integration id for an in-process provider, plugin id for a remote one.
	string ProviderId { get; }

	Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command, CancellationToken cancellationToken);

	Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken);

	// Asks for a full tree. The snapshot arrives later through the sink, never as a result
	// here.
	Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken);

	Task DispatchEventAsync(string sessionId, UiSessionEventCommand command, CancellationToken cancellationToken);
}

public sealed record UiSessionOpenCommand
{
	public required string SessionId { get; init; }

	public required UiSurface Surface { get; init; }

	public required int UiModelVersion { get; init; }
}

public sealed record UiSessionOpenOutcome
{
	public required bool Accepted { get; init; }

	public int NegotiatedUiModelVersion { get; init; }

	public string? RejectionCode { get; init; }

	public string? RejectionMessage { get; init; }

	public static UiSessionOpenOutcome Accept(int negotiatedVersion)
		=> new() { Accepted = true, NegotiatedUiModelVersion = negotiatedVersion };

	public static UiSessionOpenOutcome Reject(string code, string? message = null)
		=> new() { Accepted = false, RejectionCode = code, RejectionMessage = message };
}

public sealed record UiSessionEventCommand
{
	public required string NodeId { get; init; }

	public required string Name { get; init; }

	public UiRawJson Data { get; init; }

	public int? Revision { get; init; }

	// Which attached client acted. Meaningful only for a shared session.
	public string? ClientId { get; init; }
}

// Resolves a provider id to whichever adapter serves it. The broker never learns which kind it
// got - that is what keeps routing independent of the plugin WebSocket.
public interface IUiSessionProviderResolver
{
	IUiSessionProvider? Resolve(string providerId);
}

// A dropped plugin connection fails the in-flight invoke and ends the plugin session at the same
// moment, so both causes reach the broker concurrently. Classifying the invoke failure separately is
// what keeps the client-visible code deterministic: the transport cause wins, and the failure it
// caused is never reported as a provider fault.
public sealed class UiProviderDisconnectedException : Exception
{
	public UiProviderDisconnectedException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
