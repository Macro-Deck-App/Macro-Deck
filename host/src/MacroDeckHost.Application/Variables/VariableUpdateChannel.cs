using System.Collections.Concurrent;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Variables;

// Carries pushed and polled catalog-variable updates from wherever they originate - an IVariableSink
// publish, a subscribe result, a poll read - to VariableCatalogUpdateBackgroundService, which applies them
// to the variable registry.
//
// Bounded by construction rather than by capacity: at most one pending update per (integration, resource)
// key, keyed and overwritten in place, so a chatty provider coalesces onto its own key instead of growing
// a queue or blocking the writer. DrainAvailable is synchronous and side-effect-complete, which is what
// makes it usable directly from a test without waiting on a timer.
public sealed class VariableUpdateChannel : IDisposable
{
	public readonly record struct Update(
		string IntegrationId,
		string LocalResourceId,
		object? Value,
		VariableBounds? Bounds);

	private readonly ConcurrentDictionary<(string IntegrationId, string LocalResourceId), Update> _pending = new();
	private readonly SemaphoreSlim _signal = new(0, 1);
	private int _signalled;

	public void Write(string integrationId, string localResourceId, object? value, VariableBounds? bounds = null)
	{
		_pending[(integrationId, localResourceId)] = new Update(integrationId, localResourceId, value, bounds);

		if (Interlocked.Exchange(ref _signalled, 1) == 0)
		{
			try
			{
				_signal.Release();
			}
			catch (ObjectDisposedException)
			{
			}
			catch (SemaphoreFullException)
			{
			}
		}
	}

	/// <summary>Completes once at least one update is pending. Call <see cref="DrainAvailable"/>
	/// afterwards to collect everything that coalesced while waiting.</summary>
	public async Task WaitToReadAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			// This is a singleton disposed by the DI container, and nothing guarantees that happens only
			// after the drain loop has observed its own cancellation token - a disposal that lands first
			// must read as "stop", exactly like the cancellation the drain loop already handles, not as an
			// unhandled fault.
			throw new OperationCanceledException(cancellationToken);
		}

		Interlocked.Exchange(ref _signalled, 0);
	}

	/// <summary>Removes and returns every update currently pending, coalesced to at most one per key.</summary>
	public IReadOnlyList<Update> DrainAvailable()
	{
		if (_pending.IsEmpty)
		{
			return [];
		}

		var drained = new List<Update>();
		foreach (var key in _pending.Keys.ToList())
		{
			if (_pending.TryRemove(key, out var update))
			{
				drained.Add(update);
			}
		}

		return drained;
	}

	public void Dispose() => _signal.Dispose();
}
