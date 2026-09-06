using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public interface IWidgetService
{
	Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId, WidgetEntity widget, Guid? sourceWidgetId = null);
	Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget);

	Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(Guid folderId,
		IReadOnlyList<WidgetPlacement> placements);

	Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId);

	Task<Result<WidgetEntity, WidgetError>>
		SetPinned(Guid folderId, Guid widgetId, bool pinned, PinScope? scope = null);

	Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(
		Guid folderId,
		IReadOnlyList<WidgetEntity> widgets,
		IReadOnlyList<Guid>? replaceIds = null,
		IReadOnlyList<Guid?>? sourceWidgetIds = null);

	Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds);

	Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
		Guid folderId,
		IReadOnlyList<Guid> widgetIds,
		bool pinned,
		PinScope? scope = null);
}
