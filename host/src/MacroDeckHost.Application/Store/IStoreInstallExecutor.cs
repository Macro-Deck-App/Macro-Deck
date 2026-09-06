namespace MacroDeckHost.Application.Store;

/// <summary>Runs a single queued <see cref="Operations.StoreOperation" /> to completion: downloads and
/// verifies the release artifact, then hands it to the plugin installer, icon pack restore service or
/// profile portability service, transitioning the operation through the tracker as it goes. Never throws
/// for an install failure - every failure path ends in a terminal <see cref="Operations.StoreOperation" />
/// state instead.</summary>
public interface IStoreInstallExecutor
{
	Task Execute(Guid operationId, CancellationToken cancellationToken = default);
}
