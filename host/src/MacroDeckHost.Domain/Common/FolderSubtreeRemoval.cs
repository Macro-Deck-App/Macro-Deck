using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Common;

public sealed record FolderSubtreeRemoval(
	bool Persisted,
	IReadOnlyList<FolderEntity> RemovedFolders,
	IReadOnlyList<FolderEntity> ChangedStartFolders,
	FolderSubtreeRemovalFailure? Failure = null);

public enum FolderSubtreeRemovalFailure
{
	RootNotFound,

	LastFolder,

	OnlyRootStartFolder,

	PersistFailed
}
