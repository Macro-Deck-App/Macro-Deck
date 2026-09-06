using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreOperationBody
{
	public Guid Id { get; set; }

	public StoreOperationKind Kind { get; set; }

	public StoreExtensionKind ExtensionKind { get; set; }

	public string PackageId { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string? PreviousVersion { get; set; }

	public StoreOperationState State { get; set; }

	public long BytesDownloaded { get; set; }

	public long? TotalBytes { get; set; }

	public int? EtaSeconds { get; set; }

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	public DateTimeOffset? CompletedAt { get; set; }

	public StoreOperationError? Error { get; set; }

	public string? ErrorMessage { get; set; }

	public bool CanRetry { get; set; }
}
