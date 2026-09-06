using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public interface IProfileService
{
	Task<Result<ProfileEntity, ProfileError>> Create(
		string name,
		ProfileLayoutType? layoutType = null,
		int? defaultRows = null,
		int? defaultColumns = null,
		string? defaultBackgroundColor = null,
		int? defaultWidgetSpacing = null,
		int? defaultWidgetBorderRadius = null);

	Task<Result<ProfileEntity, ProfileError>> Update(
		Guid id,
		string? name,
		int? order,
		int? defaultRows,
		int? defaultColumns,
		string? defaultBackgroundColor,
		int? defaultWidgetSpacing,
		int? defaultWidgetBorderRadius);

	Task<Result<ProfileError>> Delete(Guid id);
}
