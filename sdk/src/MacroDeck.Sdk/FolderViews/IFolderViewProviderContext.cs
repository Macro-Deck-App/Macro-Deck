namespace MacroDeck.Sdk.FolderViews;

/// <summary>
/// The host surface a folder view provider registers against. Handed to
/// <see cref="IFolderViewProvider.InitializeAsync" /> and safe to retain for as long as the integration
/// runs.
/// </summary>
public interface IFolderViewProviderContext
{
	/// <summary>
	/// Registers a folder view, or replaces one already registered under the same provider-local id.
	/// Replacing is how a view's name, description or configuration flag changes: folders that selected it
	/// pick the new descriptor up without being touched.
	/// </summary>
	/// <returns>The host-assigned identity, whose
	/// <see cref="FolderViewRegistration.FolderViewId" /> is what a folder stores.</returns>
	/// <exception cref="ArgumentException">The descriptor's id or name is empty, or the id is not a valid
	/// local id.</exception>
	Task<FolderViewRegistration> RegisterFolderViewAsync(
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a folder view. Folders that selected it keep their stored view id and configuration and
	/// show a placeholder until it is registered again, so withdrawing never destroys a folder's setup.
	/// Unknown ids are ignored, so a provider racing a shutdown does not have to guard the call.
	/// </summary>
	/// <param name="folderViewId">The provider-local id the view was registered under.</param>
	Task UnregisterFolderViewAsync(string folderViewId, CancellationToken cancellationToken = default);
}
