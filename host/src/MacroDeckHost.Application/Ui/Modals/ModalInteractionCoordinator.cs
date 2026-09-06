using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;

namespace MacroDeckHost.Application.Ui.Modals;

/// <summary>What an action asked to be opened, before any client has opened it.</summary>
public sealed record PendingModal
{
	public required string ModalId { get; init; }

	/// <summary>The integration whose UI provider serves the dialog.</summary>
	public required string IntegrationId { get; init; }

	public required string ViewId { get; init; }

	public LocalizedText Title { get; init; }

	public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }

	/// <summary>The client the modal was addressed to.</summary>
	public required string OriginClientId { get; init; }
}

public interface IModalInteractionCoordinator
{
	/// <summary>
	/// Registers a modal and returns its id. Nothing is opened yet: the client opens the session itself,
	/// on its own authenticated connection, so the session is bound to that client's principal rather than
	/// to one the host would have had to infer from a client id.
	/// </summary>
	/// <returns>The modal id, or null when the request was refused - no client to ask, or that client
	/// already has as many modals open as it is allowed.</returns>
	string? Register(string integrationId, string? originClientId, ModalDefinition modal);

	/// <summary>Waits for the user's answer. Every way of not answering - cancellation, the client never
	/// opening it, the session faulting - resolves as a cancellation, so a caller cannot hang.</summary>
	Task<ModalResult<JsonElement>> AwaitAsync(string modalId, CancellationToken cancellationToken);

	/// <summary>Takes a registered modal for a principal that is opening it, binding it to that principal.
	/// A second principal gets nothing, so one device cannot open another device's dialog.</summary>
	bool TryClaim(string modalId, string principal, out PendingModal modal);

	/// <summary>Records the session a claimed modal is being served through, so closing the modal can close
	/// the session.</summary>
	void BindSession(string modalId, string sessionId);

	/// <summary>The session a modal is bound to, if it has been opened.</summary>
	string? SessionFor(string modalId);

	/// <summary>Settles a modal. Returns false when there is no such modal, or it belongs to another
	/// principal - a caller cannot tell those apart, which keeps this from being an enumeration
	/// oracle.</summary>
	bool Settle(string modalId, string principal, bool cancelled, JsonElement? value);

	/// <summary>Cancels a modal without a principal check - what a faulted or closed session, or a
	/// disconnecting client, leaves behind.</summary>
	void CancelInternal(string modalId);

	/// <summary>Cancels every modal bound to a session id.</summary>
	void CancelForSession(string sessionId);

	/// <summary>Cancels every modal addressed to a client - what a client going away leaves behind.</summary>
	void CancelForClient(string clientId);

	/// <summary>
	/// Cancels and forgets modals that have been registered for longer than any run could still be
	/// waiting on them. Without this a fire-and-forget modal whose client never opened it would sit in the
	/// map for the lifetime of the process: nobody awaits it, so nothing else ever removes it.
	/// </summary>
	void SweepExpired();
}

public sealed class ModalInteractionCoordinator : IModalInteractionCoordinator
{
	// A dialog interrupts whatever the user is doing, so several stacked on one client is already a bug in
	// the integration rather than a workload to support. The cap turns that bug into a refused call
	// instead of a screen the user cannot get out of.
	private const int MaxOpenModalsPerClient = 3;

	// Comfortably past the host's own maximum flow run duration, so this only ever collects a modal no
	// caller could still be waiting on.
	private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(15);

	private readonly ConcurrentDictionary<string, Entry> _byModalId = new(StringComparer.Ordinal);
	private readonly TimeProvider _timeProvider;

	public ModalInteractionCoordinator(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public string? Register(string integrationId, string? originClientId, ModalDefinition modal)
	{
		ArgumentException.ThrowIfNullOrEmpty(integrationId);
		ArgumentNullException.ThrowIfNull(modal);

		if (string.IsNullOrEmpty(originClientId) || string.IsNullOrEmpty(modal.ViewId))
		{
			return null;
		}

		if (CountFor(originClientId) >= MaxOpenModalsPerClient)
		{
			return null;
		}

		var modalId = Guid.NewGuid().ToString("N");
		_byModalId[modalId] = new Entry(_timeProvider.GetUtcNow(),
			new PendingModal
			{
				ModalId = modalId,
				IntegrationId = integrationId,
				ViewId = modal.ViewId,
				Title = modal.Title ?? default,
				Data = modal.Data,
				OriginClientId = originClientId
			});

		return modalId;
	}

	public async Task<ModalResult<JsonElement>> AwaitAsync(string modalId, CancellationToken cancellationToken)
	{
		if (!_byModalId.TryGetValue(modalId, out var entry))
		{
			return ModalResult.FromCancellation<JsonElement>();
		}

		// The entry is held locally, so removing it from the map - here or from whichever settle got there
		// first - never loses the answer this caller is waiting for.
		try
		{
			await using var registration = cancellationToken
				.Register(static state => ((Entry)state!).Cancel(), entry)
				.ConfigureAwait(false);

			return await entry.Completion.Task.ConfigureAwait(false);
		}
		finally
		{
			_byModalId.TryRemove(modalId, out _);
		}
	}

	public bool TryClaim(string modalId, string principal, out PendingModal modal)
	{
		modal = null!;

		if (!_byModalId.TryGetValue(modalId, out var entry))
		{
			return false;
		}

		if (!entry.TryBindPrincipal(principal ?? string.Empty))
		{
			return false;
		}

		modal = entry.Modal;
		return true;
	}

	public void BindSession(string modalId, string sessionId)
	{
		if (_byModalId.TryGetValue(modalId, out var entry))
		{
			entry.SessionId = sessionId;
		}
	}

	public string? SessionFor(string modalId)
		=> _byModalId.TryGetValue(modalId, out var entry) ? entry.SessionId : null;

	public bool Settle(string modalId, string principal, bool cancelled, JsonElement? value)
	{
		if (!_byModalId.TryGetValue(modalId, out var entry) ||
			!entry.BelongsTo(principal ?? string.Empty))
		{
			return false;
		}

		entry.Settle(cancelled, value);

		// Removed on settle, not only when a waiter finishes: a fire-and-forget modal has no waiter at
		// all, and its entry would otherwise stay behind after the user had already answered it.
		_byModalId.TryRemove(modalId, out _);
		return true;
	}

	public void CancelInternal(string modalId)
	{
		if (_byModalId.TryRemove(modalId, out var entry))
		{
			entry.Cancel();
		}
	}

	public void CancelForSession(string sessionId)
	{
		if (string.IsNullOrEmpty(sessionId))
		{
			return;
		}

		foreach (var (modalId, entry) in _byModalId)
		{
			if (string.Equals(entry.SessionId, sessionId, StringComparison.Ordinal))
			{
				_byModalId.TryRemove(modalId, out _);
				entry.Cancel();
			}
		}
	}

	public void CancelForClient(string clientId)
	{
		if (string.IsNullOrEmpty(clientId))
		{
			return;
		}

		foreach (var (modalId, entry) in _byModalId)
		{
			if (string.Equals(entry.Modal.OriginClientId, clientId, StringComparison.Ordinal))
			{
				_byModalId.TryRemove(modalId, out _);
				entry.Cancel();
			}
		}
	}

	public void SweepExpired()
	{
		var deadline = _timeProvider.GetUtcNow() - _lifetime;

		foreach (var (modalId, entry) in _byModalId)
		{
			if (entry.RegisteredAt <= deadline)
			{
				_byModalId.TryRemove(modalId, out _);
				entry.Cancel();
			}
		}
	}

	private int CountFor(string clientId)
		=> _byModalId.Values.Count(entry =>
			string.Equals(entry.Modal.OriginClientId, clientId, StringComparison.Ordinal));

	private sealed class Entry
	{
		private readonly object _gate = new();
		private string? _principal;

		public Entry(DateTimeOffset registeredAt, PendingModal modal)
		{
			RegisteredAt = registeredAt;
			Modal = modal;
		}

		public DateTimeOffset RegisteredAt { get; }

		public PendingModal Modal { get; }

		public TaskCompletionSource<ModalResult<JsonElement>> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string? SessionId { get; set; }

		public bool TryBindPrincipal(string principal)
		{
			lock (_gate)
			{
				if (_principal is null)
				{
					_principal = principal;
					return true;
				}

				return string.Equals(_principal, principal, StringComparison.Ordinal);
			}
		}

		public bool BelongsTo(string principal)
		{
			lock (_gate)
			{
				return _principal is not null && string.Equals(_principal, principal, StringComparison.Ordinal);
			}
		}

		// TrySetResult, not SetResult: a modal can be settled and cancelled at nearly the same moment - the
		// user submits as the connection drops - and the first answer is the true one.
		public void Settle(bool cancelled, JsonElement? value)
			=> Completion.TrySetResult(cancelled || value is null
				? ModalResult.FromCancellation<JsonElement>()
				: ModalResult.FromValue(value.Value.Clone()));

		public void Cancel() => Completion.TrySetResult(ModalResult.FromCancellation<JsonElement>());
	}
}
