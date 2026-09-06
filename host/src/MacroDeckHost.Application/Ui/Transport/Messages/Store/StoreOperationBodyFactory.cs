using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public static class StoreOperationBodyFactory
{
	public static StoreOperationBody Create(StoreOperation operation) => new()
	{
		Id = operation.Id,
		Kind = operation.Kind,
		ExtensionKind = operation.ExtensionKind,
		PackageId = operation.PackageId,
		Version = operation.Version,
		DisplayName = operation.DisplayName,
		PreviousVersion = operation.PreviousVersion,
		State = operation.State,
		BytesDownloaded = operation.BytesDownloaded,
		TotalBytes = operation.TotalBytes,
		EtaSeconds = operation.EtaSeconds,
		StartedAt = operation.StartedAt,
		UpdatedAt = operation.UpdatedAt,
		CompletedAt = operation.CompletedAt,
		Error = operation.Error,
		ErrorMessage = operation.ErrorMessage,
		CanRetry = operation.CanRetry
	};
}
