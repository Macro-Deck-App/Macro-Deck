namespace MacroDeck.Sdk.Profiles;

/// <summary>Read-only description of a folder in a virtual profile.</summary>
public sealed record VirtualFolderDescriptor(
	string Id,
	string Name,
	IReadOnlyList<VirtualWidgetDescriptor> Widgets,
	string? ParentId = null,
	int Order = 0);
