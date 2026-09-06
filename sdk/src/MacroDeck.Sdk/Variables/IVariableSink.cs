namespace MacroDeck.Sdk.Variables;

/// <summary>
/// Where a push-capable <see cref="IVariableProvider"/> sends values the host did not ask for. Handed to
/// <see cref="IVariableProvider.OnAttachedAsync"/> and implemented by the host, never by a provider -
/// which is why it can gain members without breaking anyone.
/// </summary>
public interface IVariableSink
{
	/// <summary>
	/// Publishes values for variables the host most recently subscribed to. Ids outside that set are
	/// dropped silently rather than faulting the call: the two ends resubscribe asynchronously, so a
	/// value for an id the host just unbound is a race, not an error.
	///
	/// <para>
	/// Prefer one call carrying many values to many calls carrying one - a batch is a single message, and
	/// the host bounds how many callbacks a plugin may make per second. A batch larger than the
	/// protocol's per-message bound is split by the transport, not rejected.
	/// </para>
	///
	/// <para>
	/// Publishing is fire-and-forget from the provider's point of view: the returned task completes when
	/// the host has accepted the batch, and a failed delivery is logged rather than thrown, so a
	/// provider's event loop cannot be taken down by a transient transport problem.
	/// </para>
	/// </summary>
	Task PublishAsync(
		IReadOnlyCollection<VariableValue> values,
		CancellationToken cancellationToken = default);

	/// <summary>Publishes a single value.</summary>
	Task PublishAsync(VariableValue value, CancellationToken cancellationToken = default)
		=> PublishAsync([value], cancellationToken);

	/// <summary>
	/// Tells the host that the set of resources this provider can enumerate has changed. Currently
	/// accepted and recorded, but nothing in the host acts on it yet - no open browser re-queries as a
	/// result. Call it anyway when the provider's catalog changes: a future host release is expected to
	/// start reacting to it, and a provider that already calls it correctly needs no change when that
	/// lands.
	/// </summary>
	Task InvalidateCatalogAsync(CancellationToken cancellationToken = default);
}
