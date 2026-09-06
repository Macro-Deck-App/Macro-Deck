using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

/// <summary>
/// Correlates a modal the plugin opened with the answer that arrives later. Two capabilities meet here:
/// <c>actions</c> opens the modal and waits, <c>ui</c> receives the <c>modal.result</c> that ends the
/// wait, so neither can own the correlation and it is a shared singleton instead.
/// </summary>
/// <remarks>
/// The wait is deliberately unbounded here. It is bounded on the host side - by the flow's own
/// cancellation and its maximum run duration - and the caller passes that cancellation in, so bounding it
/// a second time with a timeout of this side's own choosing could only ever cut a modal short that the
/// host still considered live.
/// </remarks>
internal sealed class ModalResultStore
{
	private readonly ConcurrentDictionary<string, TaskCompletionSource<UiModalResultArguments>> _pending =
		new(StringComparer.Ordinal);

	public Task<UiModalResultArguments> Await(string modalId, CancellationToken cancellationToken)
	{
		var completion = _pending.GetOrAdd(modalId,
			static _ => new TaskCompletionSource<UiModalResultArguments>(TaskCreationOptions
				.RunContinuationsAsynchronously));

		return WaitAsync(modalId, completion, cancellationToken);
	}

	/// <summary>Delivers an answer. Unknown ids are ignored: a result for a wait that has already been
	/// cancelled is late, not wrong.</summary>
	public void Complete(UiModalResultArguments result)
	{
		ArgumentNullException.ThrowIfNull(result);

		if (_pending.TryRemove(result.ModalId, out var completion))
		{
			completion.TrySetResult(result);
		}
	}

	/// <summary>Cancels every outstanding wait - what losing the connection to the host leaves behind,
	/// since the answer would have travelled over it.</summary>
	public void CancelAll()
	{
		foreach (var modalId in _pending.Keys)
		{
			if (_pending.TryRemove(modalId, out var completion))
			{
				completion.TrySetResult(new UiModalResultArguments { ModalId = modalId, Cancelled = true });
			}
		}
	}

	private async Task<UiModalResultArguments> WaitAsync(
		string modalId,
		TaskCompletionSource<UiModalResultArguments> completion,
		CancellationToken cancellationToken)
	{
		try
		{
			await using var registration = cancellationToken
				.Register(() => completion.TrySetResult(new UiModalResultArguments
				{
					ModalId = modalId, Cancelled = true
				}))
				.ConfigureAwait(false);

			return await completion.Task.ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(modalId, out _);
		}
	}
}
