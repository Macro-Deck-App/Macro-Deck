namespace MacroDeckHost.Application.Ui.Sessions;

// Where a provider's output enters the host. Every member is synchronous and returns the ingest verdict
// immediately, so a remote provider learns from its own host.result that a payload was rejected,
// and so the plugin connection's serial inbound pump is never blocked on a client write.
public interface IUiSessionSink
{
	UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree);

	UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch);

	void PublishFault(string providerId, string sessionId, string code, string? message);
}

public readonly record struct UiSessionIngestResult
{
	public required bool Accepted { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	public static UiSessionIngestResult Accept() => new() { Accepted = true };

	public static UiSessionIngestResult Reject(string code, string? message = null)
		=> new() { Accepted = false, Code = code, Message = message };
}
