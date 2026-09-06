using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

/// <param name="CornerRadiusChanged">Whether the save changed the radius the folder's tiles are drawn
/// with. Every widget in the folder builds its own edge clearance from that radius (ADR 0064), so a
/// change to it is one of the few folder edits that has to reach the widgets themselves.</param>
public sealed record FolderUpdatedNotification(FolderEntity Folder, bool CornerRadiusChanged = false)
	: INotification;
