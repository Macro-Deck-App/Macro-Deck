using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public interface IFolderService
{
	/// <param name="folderViewId">Which folder view renders the folder. Null selects the built-in widget
	/// grid.</param>
	/// <param name="folderViewConfiguration">The view's configuration, as JSON object text. Ignored when
	/// the folder is a widget grid, which configures nothing.</param>
	Task<Result<FolderEntity, FolderError>> Create(
		Guid profileId,
		string name,
		Guid? parentId,
		string? folderViewId = null,
		string? folderViewConfiguration = null);

	/// <param name="folderViewId">Absent leaves the view unchanged. Any other value switches to it, which
	/// clears the previous view's configuration unless <paramref name="folderViewConfiguration" /> supplies
	/// a new one - a configuration means nothing to a view that did not write it.</param>
	/// <param name="folderViewConfiguration">Absent leaves the configuration unchanged.</param>
	Task<Result<FolderEntity, FolderError>> Update(
		Guid id,
		string? name,
		Guid? parentId,
		int? order,
		int? rows,
		int? columns,
		string? backgroundColor,
		int? widgetSpacing,
		int? widgetBorderRadius,
		bool? isDefault,
		string? folderViewId = null,
		string? folderViewConfiguration = null);

	Task<Result<FolderError>> Delete(Guid id);
	Task<Result<FolderEntity, FolderError>> Duplicate(Guid id);

	Task<Result<IReadOnlyList<FolderEntity>, FolderError>> Move(Guid id, Guid targetId, FolderMovePosition position);

	Task<Result<FolderFocusRule, FolderError>> SetFocusRule(Guid folderId, FolderFocusRule rule);

	Task<Result<FolderError>> DeleteFocusRule(Guid folderId, Guid ruleId);
}
