using System.Collections.Concurrent;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Events;
using Mediator;

namespace MacroDeckHost.Application.Layouts;

public sealed class LayoutRegistry : ILayoutRegistry
{
	private readonly ConcurrentDictionary<string, LayoutDescriptor> _byQualifiedId = new(StringComparer.Ordinal);
	private readonly IPublisher _publisher;

	public LayoutRegistry(IPublisher publisher)
	{
		_publisher = publisher;
	}

	public async Task<LayoutRegistration> Register(
		string ownerId,
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(layout);

		if (!QualifiedId.TryCreate(ownerId, layout.Id, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			throw new ArgumentException(
				$"'{ownerId}' is not a valid owner id, or '{layout.Id}' is not a valid layout id.",
				nameof(layout));
		}

		if (string.IsNullOrWhiteSpace(layout.Name))
		{
			throw new ArgumentException("The layout name must not be empty.", nameof(layout));
		}

		var seenRegionIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var region in layout.Regions)
		{
			if (string.IsNullOrWhiteSpace(region.Id))
			{
				throw new ArgumentException("A layout region id must not be empty.", nameof(layout));
			}

			if (!seenRegionIds.Add(region.Id))
			{
				throw new ArgumentException($"Region id '{region.Id}' is repeated within the layout.",
					nameof(layout));
			}
		}

		var qualifiedId = id.ToString();
		_byQualifiedId[qualifiedId] = layout;

		await _publisher.Publish(new LayoutCatalogChangedNotification(), cancellationToken);

		return new LayoutRegistration(qualifiedId, ownerId);
	}

	public async Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default)
	{
		if (!QualifiedId.TryCreate(ownerId, localId, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			return;
		}

		if (_byQualifiedId.TryRemove(id.ToString(), out _))
		{
			await _publisher.Publish(new LayoutCatalogChangedNotification(), cancellationToken);
		}
	}

	public async Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default)
	{
		var removedAny = false;
		foreach (var key in _byQualifiedId.Keys)
		{
			if (QualifiedId.TryParse(key, out var id) && string.Equals(id.OwnerId, ownerId, StringComparison.Ordinal))
			{
				removedAny |= _byQualifiedId.TryRemove(key, out _);
			}
		}

		if (removedAny)
		{
			await _publisher.Publish(new LayoutCatalogChangedNotification(), cancellationToken);
		}
	}

	public bool TryResolve(string qualifiedId, out LayoutDescriptor layout)
	{
		if (string.IsNullOrEmpty(qualifiedId))
		{
			layout = null!;
			return false;
		}

		return _byQualifiedId.TryGetValue(qualifiedId, out layout!);
	}
}
