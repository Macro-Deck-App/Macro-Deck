using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Application.Profiles;

public interface IProfileRegistry
{
	IReadOnlyList<Profile> GetProfiles();

	IReadOnlyList<Folder> GetFoldersForProfile(string profileId);

	bool IsVirtual(string profileId);

	Widget? FindVirtualWidget(string widgetId);

	Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction);
}
