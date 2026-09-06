using System.Collections.Concurrent;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Localization;
using Mediator;

namespace MacroDeckHost.Application.FolderViews;

public sealed class FolderViewRegistry : IFolderViewRegistry
{
	// Not stored in _byQualifiedId: the grid has no provider and is never opened as a UI session, so
	// putting it there would make TryResolve answer "yes, and the provider is the empty string" for the
	// one view that must never be looked up that way.
	private static readonly FolderViewCatalogEntry _widgetGrid = new(BuiltInFolderViews.WidgetGrid,
		string.Empty,
		new FolderViewDescriptor(BuiltInFolderViews.WidgetGrid,
			AppStrings.FolderViews.WidgetGrid.Name(),
			AppStrings.FolderViews.WidgetGrid.Description()));

	private readonly ConcurrentDictionary<string, FolderViewCatalogEntry> _byQualifiedId =
		new(StringComparer.Ordinal);

	private readonly IPublisher _publisher;

	public FolderViewRegistry(IPublisher publisher)
	{
		_publisher = publisher;
	}

	public async Task<FolderViewRegistration> Register(
		string ownerId,
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folderView);

		if (!QualifiedId.TryCreate(ownerId, folderView.Id, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			throw new ArgumentException(
				$"'{ownerId}' is not a valid owner id, or '{folderView.Id}' is not a valid folder view id.",
				nameof(folderView));
		}

		if (folderView.Name.IsEmpty)
		{
			throw new ArgumentException("The folder view name must not be empty.", nameof(folderView));
		}

		var qualifiedId = id.ToString();
		_byQualifiedId[qualifiedId] = new FolderViewCatalogEntry(qualifiedId, ownerId, folderView);

		await _publisher.Publish(new FolderViewCatalogChangedNotification(), cancellationToken);

		return new FolderViewRegistration(qualifiedId, ownerId);
	}

	public async Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default)
	{
		if (!QualifiedId.TryCreate(ownerId, localId, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			return;
		}

		if (_byQualifiedId.TryRemove(id.ToString(), out _))
		{
			await _publisher.Publish(new FolderViewCatalogChangedNotification(), cancellationToken);
		}
	}

	public async Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default)
	{
		var removedAny = false;
		foreach (var (key, entry) in _byQualifiedId)
		{
			if (string.Equals(entry.ProviderId, ownerId, StringComparison.Ordinal))
			{
				removedAny |= _byQualifiedId.TryRemove(key, out _);
			}
		}

		if (removedAny)
		{
			await _publisher.Publish(new FolderViewCatalogChangedNotification(), cancellationToken);
		}
	}

	public bool TryResolve(string folderViewId, out FolderViewCatalogEntry entry)
	{
		if (string.IsNullOrEmpty(folderViewId))
		{
			entry = null!;
			return false;
		}

		return _byQualifiedId.TryGetValue(folderViewId, out entry!);
	}

	// The grid is first and the rest sort by qualified id, so the picker's order does not depend on which
	// integration happened to start first.
	public IReadOnlyList<FolderViewCatalogEntry> GetAll()
		=>
		[
			_widgetGrid,
			.. _byQualifiedId.Values.OrderBy(candidate => candidate.FolderViewId, StringComparer.Ordinal),
		];
}
