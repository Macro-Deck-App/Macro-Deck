using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Application.Profiles;

public interface IProfileRegistry
{
	IReadOnlyList<Profile> GetProfiles();

	IReadOnlyList<Folder> GetFoldersForProfile(string profileId);

	bool IsVirtual(string profileId);

	Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction);
}
