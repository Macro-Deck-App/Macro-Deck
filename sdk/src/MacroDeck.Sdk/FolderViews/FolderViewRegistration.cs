namespace MacroDeck.Sdk.FolderViews;

/// <summary>The host's identity for a registered folder view.</summary>
/// <param name="FolderViewId">
/// The qualified id - <c>provider.id::view-id</c> - a folder stores to select this view. The host derives
/// it; a provider never constructs it itself.
/// </param>
/// <param name="ProviderId">The integration or plugin that owns the view.</param>
public sealed record FolderViewRegistration(string FolderViewId, string ProviderId);
