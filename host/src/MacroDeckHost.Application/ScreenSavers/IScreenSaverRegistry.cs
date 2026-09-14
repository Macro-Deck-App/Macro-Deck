using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeckHost.Application.ScreenSavers;

public sealed record ScreenSaverCatalogEntry(
	string ScreenSaverId,
	string ProviderId,
	ScreenSaverDescriptor Descriptor);

public interface IScreenSaverRegistry
{
	Task<ScreenSaverRegistration> Register(string ownerId,
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default);

	Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default);

	Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default);

	bool TryResolve(string screenSaverId, out ScreenSaverCatalogEntry entry);

	IReadOnlyList<ScreenSaverCatalogEntry> GetAll();
}
