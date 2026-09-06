namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// A complete profile supplied by an integration: a fixed layout plus its folders and widgets. The
/// host surfaces it read-only (no JSON file is created) alongside the user's own profiles.
/// </summary>
public sealed record VirtualProfileDescriptor(
	string Id,
	string Name,
	ProfileLayout Layout,
	IReadOnlyList<VirtualFolderDescriptor> Folders);
