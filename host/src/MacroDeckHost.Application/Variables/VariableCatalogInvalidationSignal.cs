namespace MacroDeckHost.Application.Variables;

// Fans out IVariableSink.InvalidateCatalogAsync to whatever UI-facing layer wants to re-query a
// provider's browse tree. A plain event rather than a new pub-sub dependency: the sink raises it once per
// invalidation, coalescing is the listener's business.
//
// No production code currently subscribes to Invalidated - see the "invalidate" row in
// docs/src/content/docs/reference/websocket.md and IVariableSink.InvalidateCatalogAsync's own
// remarks, which both spell out that the callback is accepted but nothing acts on it yet.
public sealed class VariableCatalogInvalidationSignal
{
	public event Action<string>? Invalidated;

	public void RaiseInvalidated(string integrationId) => Invalidated?.Invoke(integrationId);
}
