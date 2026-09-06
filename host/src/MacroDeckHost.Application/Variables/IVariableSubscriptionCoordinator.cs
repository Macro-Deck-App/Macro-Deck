namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Owns the desired catalog-variable working set: every binding whose provider currently resolves.
/// Reconciling calls <see cref="MacroDeck.Sdk.Variables.IVariableProvider.SubscribeAsync"/> with the
/// complete set for each integration - it replaces, never increments - and feeds the results into the
/// update channel so a freshly bound or freshly reconnected resource gets its value without a separate
/// read.
/// </summary>
public interface IVariableSubscriptionCoordinator
{
	/// <summary>
	/// Recomputes the working set from the current bindings and every provider's current availability, and
	/// pushes the result to each affected provider and into the update channel. Idempotent: calling it
	/// again with nothing changed re-subscribes the same set.
	/// </summary>
	Task ReconcileAsync(CancellationToken cancellationToken = default);

	/// <summary>Whether <paramref name="localResourceId"/> is part of the working set most recently declared
	/// to <paramref name="integrationId"/>'s provider. Used by <see cref="HostVariableSink"/> to drop
	/// a push for an id the host is not currently watching.</summary>
	bool IsSubscribed(string integrationId, string localResourceId);
}
