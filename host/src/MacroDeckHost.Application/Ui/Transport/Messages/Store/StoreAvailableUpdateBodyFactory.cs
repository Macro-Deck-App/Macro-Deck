using MacroDeckHost.Application.Store.Updates;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public static class StoreAvailableUpdateBodyFactory
{
	public static StoreAvailableUpdateBody Create(StoreAvailableUpdate update) => new()
	{
		Kind = update.Kind,
		PackageId = update.PackageId,
		Name = update.Name,
		InstalledVersion = update.InstalledVersion,
		LatestVersion = update.LatestVersion
	};
}
