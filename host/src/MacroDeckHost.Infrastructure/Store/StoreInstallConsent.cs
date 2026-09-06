using System.Collections.Concurrent;

namespace MacroDeckHost.Infrastructure.Store;

/// <summary>Carries unsigned consent from the install call that the user triggered to the worker that
/// runs it, keyed on the operation id. In memory only and consumed once: consent is a decision about one
/// install, so it must not outlive the process, be persisted alongside the operation, or be inherited by
/// a retry - a retry is a fresh operation and asks again.</summary>
public sealed class StoreInstallConsent
{
	private readonly ConcurrentDictionary<Guid, byte> _consented = new();

	public void Record(Guid operationId) => _consented[operationId] = 0;

	public bool Consume(Guid operationId) => _consented.TryRemove(operationId, out _);
}
