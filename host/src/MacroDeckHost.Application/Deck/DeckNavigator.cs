using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeck.Sdk.Decks;

namespace MacroDeckHost.Application.Deck;

public sealed class DeckNavigator : IDeckNavigator, IDeviceDeckNavigator
{
	private readonly IUiTransport _transport;
	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IProfileRegistry _profileRegistry;

	// Resolved lazily: the surface service reports folder changes through the focus coordinator, which
	// navigates through this navigator, so the two cannot be constructed as a straight dependency.
	private readonly Func<IDeviceSurfaceService> _deviceSurfaces;

	public DeckNavigator(
		IUiTransport transport,
		IFolderCache folderCache,
		IProfileCache profileCache,
		IProfileRegistry profileRegistry,
		Func<IDeviceSurfaceService> deviceSurfaces)
	{
		_transport = transport;
		_folderCache = folderCache;
		_profileCache = profileCache;
		_profileRegistry = profileRegistry;
		_deviceSurfaces = deviceSurfaces;
	}

	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		// A device session validates against its own profile's folders instead: the folder cache holds
		// no virtual profile's folders, and a device assigned one must still be able to navigate it.
		if (DeviceOrigin.TryParse(originClientId, out var deviceId))
		{
			return _deviceSurfaces().NavigateAsync(deviceId,
				DeckNavigationCommand.ChangeTo(folderId, profileId: null),
				cancellationToken);
		}

		// A recursively deleted folder can still be the target of a stale "Change Folder" action
		// (flow JSON is not rewritten on delete); silently doing nothing is the graceful outcome.
		if (!Guid.TryParse(folderId, out var id) || _folderCache.GetFolderById(id) is not { } folder)
		{
			return Task.CompletedTask;
		}

		return Publish(new FolderNavigationEvent
			{
				Command = "changeTo",
				FolderId = folderId,
				ProfileId = folder.ProfileId.ToString()
			},
			originClientId);
	}

	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		// Same deliberate stale-target tolerance as ChangeFolderAsync (issue #282): a profile deleted
		// after the flow was authored must not push a broken navigation.
		if (StartFolderResolver.Resolve(_profileRegistry, profileId) is not { } folder)
		{
			return Task.CompletedTask;
		}

		return Publish(new FolderNavigationEvent
			{
				Command = "changeTo",
				FolderId = folder.Id,
				ProfileId = profileId
			},
			originClientId);
	}

	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Publish(new FolderNavigationEvent { Command = "parent" }, originClientId);

	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Publish(new FolderNavigationEvent { Command = "back" }, originClientId);

	public async Task<bool> ChangeFolderOnDeviceAsync(Guid deviceId,
		Guid folderId,
		Guid navigationToken,
		CancellationToken cancellationToken)
	{
		var surfaces = _deviceSurfaces();
		if (surfaces.IsOpen(deviceId))
		{
			return await surfaces.NavigateAsync(deviceId,
				DeckNavigationCommand.ChangeTo(folderId.ToString(),
					profileId: null,
					navigationToken.ToString("D")),
				cancellationToken);
		}

		// Same graceful no-op as ChangeFolderAsync: a stale target must not push a broken navigation.
		if (_folderCache.GetFolderById(folderId) is not { } folder)
		{
			return false;
		}

		await _transport.SendToGroup(UiDeviceGroups.For(deviceId),
			new FolderNavigationEvent
			{
				Command = "changeTo",
				FolderId = folderId.ToString(),
				ProfileId = folder.ProfileId.ToString(),
				NavigationToken = navigationToken.ToString("D")
			},
			cancellationToken);

		return true;
	}

	public async Task<bool> ChangeProfileOnDeviceAsync(Guid deviceId,
		string profileId,
		CancellationToken cancellationToken)
	{
		// Same graceful no-op as ChangeProfileAsync: an unresolvable profile must not push a broken navigation.
		if (StartFolderResolver.Resolve(_profileRegistry, profileId) is not { } folder)
		{
			return false;
		}

		var surfaces = _deviceSurfaces();
		if (surfaces.IsOpen(deviceId))
		{
			return await surfaces.NavigateAsync(deviceId,
				DeckNavigationCommand.ChangeTo(folder.Id, profileId),
				cancellationToken);
		}

		await _transport.SendToGroup(UiDeviceGroups.For(deviceId),
			new FolderNavigationEvent
			{
				Command = "changeTo",
				FolderId = folder.Id,
				ProfileId = profileId,
				// Deliberately no token - see IDeviceDeckNavigator.ChangeProfileOnDeviceAsync.
				NavigationToken = null
			},
			cancellationToken);

		return true;
	}

	public IReadOnlyList<DeckFolder> GetFolders()
	{
		var profileNames = _profileCache.GetAll().ToDictionary(p => p.Id, p => p.Name);
		var multipleProfiles = profileNames.Count > 1;

		return _folderCache.GetAllFolders()
			.Select(folder => new DeckFolder
			{
				Id = folder.Id.ToString(),
				Label = multipleProfiles && profileNames.TryGetValue(folder.ProfileId, out var profileName)
					? $"{profileName} / {folder.Name}"
					: folder.Name
			})
			.OrderBy(folder => folder.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public IReadOnlyList<DeckProfile> GetProfiles()
		=> _profileRegistry.GetProfiles()
			.Select(profile => new DeckProfile { Id = profile.Id, Label = profile.Name })
			.OrderBy(profile => profile.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();

	// A device origin never reaches the transport: the navigation is applied to that one session and is
	// invisible to every WebSocket client, including the admin UI and any other device.
	private Task Publish(FolderNavigationEvent navigation, string? originClientId)
	{
		if (DeviceOrigin.TryParse(originClientId, out var deviceId))
		{
			return _deviceSurfaces().NavigateAsync(deviceId,
				new DeckNavigationCommand
				{
					Command = navigation.Command,
					FolderId = navigation.FolderId,
					ProfileId = navigation.ProfileId,
					NavigationToken = navigation.NavigationToken
				},
				CancellationToken.None);
		}

		return string.IsNullOrEmpty(originClientId)
			? _transport.Send(navigation)
			: _transport.SendToGroup(UiClientGroups.For(originClientId), navigation);
	}
}
