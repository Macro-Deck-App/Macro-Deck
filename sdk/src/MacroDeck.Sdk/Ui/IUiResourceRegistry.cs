using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Sdk.Ui;

/// <summary>
/// Turns bytes into a <see cref="UiResource" /> an image or button in this integration's own trees can
/// show. Reached through <see cref="IIntegrationContext.UiResources" />.
/// </summary>
/// <remarks>
/// <para>
/// <b>Names.</b> A name is chosen by the integration: an ASCII letter or digit followed by up to 63 more
/// letters, digits, hyphens or underscores. Registering a name again replaces its bytes. The handle keeps
/// its <see cref="UiResource.ResourceId" /> and gets a new <see cref="UiResource.ContentHash" />, which is
/// what makes clients fetch the new bytes, so always put the handle this call returned into the tree rather
/// than one built by hand.
/// </para>
/// <para>
/// <b>Limits.</b> One resource is at most <c>maxUiResourceBytes</c> (2 MiB); all of a plugin's resources
/// together are at most <c>maxUiResourceBytesPerPlugin</c> and <c>maxUiResourcesPerPlugin</c>. Media types
/// are PNG, JPEG, WebP and GIF.
/// </para>
/// <para>
/// <b>Lifetime.</b> Resources are kept in memory by Macro Deck for as long as the plugin's session lasts,
/// including a reconnect that resumes it and a re-initialisation after a configuration change. They are
/// released when the session ends and are gone after Macro Deck restarts, so register them in
/// <see cref="IIntegration.InitializeAsync" />, or before building a tree that references them.
/// </para>
/// </remarks>
public interface IUiResourceRegistry
{
	/// <summary>
	/// Registers <paramref name="content" /> under <paramref name="name" />, replacing what the name held,
	/// and returns the handle to reference from a tree. Calls are carried out one at a time.
	/// </summary>
	/// <exception cref="ArgumentException">The name is not valid, the media type is not supported, or the
	/// content is empty or larger than one resource may be. Nothing was sent.</exception>
	/// <exception cref="UiResourceException">Macro Deck refused or could not complete the registration; see
	/// <see cref="UiResourceException.ErrorCode" />. What the name held before is unchanged.</exception>
	Task<UiResource> RegisterAsync(string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default);

	/// <summary>Removes the resource registered under <paramref name="name" /> and frees its share of the
	/// quota. A name that holds nothing is not an error.</summary>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	/// <exception cref="UiResourceException">Macro Deck could not carry out the removal.</exception>
	Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}
