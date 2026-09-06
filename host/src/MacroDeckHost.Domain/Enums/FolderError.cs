namespace MacroDeckHost.Domain.Enums;

public enum FolderError
{
	ValidationError,
	NotFound,
	AlreadyExists,
	InvalidParent,
	CannotDeleteLastFolder,
	GridTooSmall,
	GridLockedByDevice,
	DuplicateFocusRule,
	InternalError,

	PinnedWidgetConflict,

	UnknownFolderView,

	InvalidFolderViewConfiguration
}
