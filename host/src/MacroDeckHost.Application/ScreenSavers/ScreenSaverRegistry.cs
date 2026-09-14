using System.Collections.Concurrent;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using Mediator;

namespace MacroDeckHost.Application.ScreenSavers;

public sealed class ScreenSaverRegistry : IScreenSaverRegistry
{
	private readonly ConcurrentDictionary<string, ScreenSaverCatalogEntry> _byQualifiedId =
		new(StringComparer.Ordinal);

	private readonly IPublisher _publisher;

	// Built-ins are seeded from the catalog the provider already declares, exactly what a plugin is asked
	// for after a reconnect, so they exist before the first request rather than after a hosted service.
	public ScreenSaverRegistry(IPublisher publisher, IEnumerable<IScreenSaverProvider>? builtIns = null)
	{
		_publisher = publisher;

		foreach (var provider in builtIns ?? [])
		{
			if (provider is not IBuiltInIntegrationUiProvider owner)
			{
				continue;
			}

			foreach (var screenSaver in provider.GetScreenSavers())
			{
				if (QualifiedId.TryCreate(owner.IntegrationId, screenSaver.Id, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
				{
					_byQualifiedId[id.ToString()] = new ScreenSaverCatalogEntry(id.ToString(), owner.IntegrationId, screenSaver);
				}
			}
		}
	}

	public async Task<ScreenSaverRegistration> Register(
		string ownerId,
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(screenSaver);

		// The built-in owner is seeded, never registered: a plugin under that id could otherwise replace
		// the clock every device falls back to, and take it away again on its uninstall.
		if (string.Equals(ownerId, BuiltInScreenSavers.ProviderId, StringComparison.Ordinal))
		{
			throw new ArgumentException($"'{ownerId}' is reserved for Macro Deck's own screensavers.", nameof(ownerId));
		}

		if (!QualifiedId.TryCreate(ownerId, screenSaver.Id, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			throw new ArgumentException(
				$"'{ownerId}' is not a valid owner id, or '{screenSaver.Id}' is not a valid screensaver id.",
				nameof(screenSaver));
		}

		if (screenSaver.Name.IsEmpty)
		{
			throw new ArgumentException("The screensaver name must not be empty.", nameof(screenSaver));
		}

		var qualifiedId = id.ToString();
		_byQualifiedId[qualifiedId] = new ScreenSaverCatalogEntry(qualifiedId, ownerId, screenSaver);

		await _publisher.Publish(new ScreenSaverCatalogChangedNotification(), cancellationToken);

		return new ScreenSaverRegistration(qualifiedId, ownerId);
	}

	public async Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default)
	{
		if (!QualifiedId.TryCreate(ownerId, localId, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			return;
		}

		if (_byQualifiedId.TryRemove(id.ToString(), out _))
		{
			await _publisher.Publish(new ScreenSaverCatalogChangedNotification(), cancellationToken);
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
			await _publisher.Publish(new ScreenSaverCatalogChangedNotification(), cancellationToken);
		}
	}

	public bool TryResolve(string screenSaverId, out ScreenSaverCatalogEntry entry)
	{
		if (string.IsNullOrEmpty(screenSaverId))
		{
			entry = null!;
			return false;
		}

		return _byQualifiedId.TryGetValue(screenSaverId, out entry!);
	}

	public IReadOnlyList<ScreenSaverCatalogEntry> GetAll()
		=>
		[
			.. _byQualifiedId.Values
				.OrderBy(candidate => BuiltInScreenSavers.IsBuiltIn(candidate.ScreenSaverId) ? 0 : 1)
				.ThenBy(candidate => candidate.ScreenSaverId, StringComparer.Ordinal),
		];
}
