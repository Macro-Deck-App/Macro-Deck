using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;

/// <summary>One entry in the folder view picker.</summary>
public sealed record FolderView
{
	/// <summary>The id a folder stores to select this view.</summary>
	public required string Id { get; init; }

	/// <summary>The integration that provides it. Empty for the built-in widget grid.</summary>
	public string ProviderId { get; init; } = string.Empty;

	public LocalizedText Name { get; init; }

	public LocalizedText Description { get; init; }

	/// <summary>See the SDK's <c>FolderViewNavigation</c>. Always resolved, never absent - a client must
	/// not have to decide what an unrecognised mode means.</summary>
	public required string Navigation { get; init; }

	/// <summary>Whether choosing this view opens a configuration surface.</summary>
	public bool HasConfiguration { get; init; }

	/// <summary>Whether the client renders this view itself rather than as a UI session. True only for the
	/// built-in widget grid.</summary>
	public bool IsBuiltIn { get; init; }
}

public sealed record GetFolderViewsRequest;

public sealed record GetFolderViewsResponse
{
	public IReadOnlyList<FolderView> FolderViews { get; init; } = [];
}

/// <summary>A provider registered or withdrew folder views. Carries the whole catalog, so a client
/// replaces rather than reconciles.</summary>
public sealed record FolderViewCatalogChangedEvent
{
	public IReadOnlyList<FolderView> FolderViews { get; init; } = [];
}
