using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Layouts;

/// <summary>
/// The host side of the layout provider contract: the live, in-memory catalogue of layouts an
/// in-process integration or a connected plugin currently offers. Persistence of what a device last
/// resolved to lives on the device itself, not here - see <c>DeviceEntity.LayoutSnapshot</c>.
/// </summary>
public interface ILayoutRegistry
{
	/// <summary>
	/// Registers a layout, or replaces one already registered under the same owner and local id.
	/// </summary>
	/// <exception cref="ArgumentException">The owner id, the descriptor's id or name is invalid or
	/// empty, or a region id is empty or repeated within the layout.</exception>
	Task<LayoutRegistration> Register(string ownerId,
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default);

	/// <summary>Withdraws a layout. Unknown ids are ignored.</summary>
	Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default);

	/// <summary>Withdraws every layout of one owner - what a stopping integration or a dropped plugin
	/// session leaves behind.</summary>
	Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default);

	/// <summary>Resolves a qualified id against the live registry only. Never throws.</summary>
	bool TryResolve(string qualifiedId, out LayoutDescriptor layout);
}
