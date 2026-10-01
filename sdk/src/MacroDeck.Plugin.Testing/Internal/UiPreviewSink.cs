using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Testing.Internal;

internal sealed class UiPreviewSink
{
	private readonly ConcurrentDictionary<string, TaskCompletionSource<UiPreviewFrame>> _waiting
		= new(StringComparer.Ordinal);

	private volatile bool _attached;

	public bool Attached => _attached;

	public Task<UiPreviewFrame> Expect(string sessionId)
	{
		_attached = true;

		return _waiting.GetOrAdd(sessionId,
			static _ => new TaskCompletionSource<UiPreviewFrame>(TaskCreationOptions.RunContinuationsAsynchronously))
			.Task;
	}

	public void Forget(string sessionId) => _waiting.TryRemove(sessionId, out _);

	public HostInvokeOutcome Dispatch(HostInvokePayload payload)
	{
		switch (payload.Operation)
		{
			case HostOperations.Ui.Snapshot when Arguments<UiSnapshotArguments>(payload) is { } snapshot:
				Complete(snapshot.SessionId, new UiPreviewFrame(snapshot.Tree.Clone(), null, null));
				break;
			case HostOperations.Ui.Fault when Arguments<UiFaultArguments>(payload) is { } fault:
				Complete(fault.SessionId, new UiPreviewFrame(null, fault.Code, fault.Message));
				break;
		}

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private void Complete(string sessionId, UiPreviewFrame frame)
	{
		if (_waiting.TryGetValue(sessionId, out var completion))
		{
			completion.TrySetResult(frame);
		}
	}

	private static T? Arguments<T>(HostInvokePayload payload) where T : class
		=> payload.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
}

internal sealed record UiPreviewFrame(JsonElement? Tree, string? FaultCode, string? FaultMessage);
